using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veyra.Core.Metrics;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// Moves queued metric points into storage in large batches, retrying while the database is unavailable, like the
/// log writer.
/// </summary>
/// <param name="queue">Where accepted points wait.</param>
/// <param name="writer">The storage module's writer.</param>
/// <param name="clock">The clock for retry pauses.</param>
/// <param name="logger">Receives write failures.</param>
internal sealed partial class MetricWriterService(
    MetricQueue queue,
    IMetricWriter writer,
    TimeProvider clock,
    ILogger<MetricWriterService> logger) : BackgroundService
{
    // Points are narrow; a large batch keeps COPY efficient and the series lookups few.
    private const int _maxBatchPoints = 10_000;

    private static readonly TimeSpan _firstRetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Writes batches until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<MetricPoint> first;

            try
            {
                first = await queue.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var batch = new List<MetricPoint>(first);

            while (batch.Count < _maxBatchPoints && queue.TryRead(out var more))
            {
                batch.AddRange(more);
            }

            await WriteWithRetryAsync(batch, stoppingToken);
        }
    }

    private async Task WriteWithRetryAsync(List<MetricPoint> batch, CancellationToken stoppingToken)
    {
        var delay = _firstRetryDelay;

        while (true)
        {
            try
            {
                await writer.WriteAsync(batch, stoppingToken);
                queue.MarkWritten(batch.Count);

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                LogDroppedOnShutdown(batch.Count);

                return;
            }
            catch (Exception exception)
            {
                LogWriteFailed(exception, batch.Count, delay);
            }

            try
            {
                await Task.Delay(delay, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                LogDroppedOnShutdown(batch.Count);

                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxRetryDelay.Ticks));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Writing {Count} metric points failed; retrying in {Delay}")]
    private partial void LogWriteFailed(Exception exception, int count, TimeSpan delay);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Veyra is stopping while {Count} metric points are still unwritten; they are lost")]
    private partial void LogDroppedOnShutdown(int count);
}
