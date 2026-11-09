using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// Moves queued log records into storage in large batches, retrying while the database is unavailable.
/// </summary>
/// <remarks>
/// A failed batch is retried with growing pauses, up to 30 seconds, and never dropped. Meanwhile the queue fills and
/// senders are asked to retry, which is OTLP's normal way to ride out an outage.
/// </remarks>
/// <param name="queue">Where accepted records wait.</param>
/// <param name="writer">The storage module's writer.</param>
/// <param name="clock">The clock for retry pauses.</param>
/// <param name="logger">Receives write failures.</param>
internal sealed partial class LogWriterService(
    LogQueue queue,
    ILogWriter writer,
    TimeProvider clock,
    ILogger<LogWriterService> logger) : BackgroundService
{
    // Large enough for COPY to be efficient, small enough to keep a single transaction short.
    private const int _maxBatchRecords = 5_000;

    private static readonly TimeSpan _firstRetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Writes batches until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<LogRecord> first;

            try
            {
                first = await queue.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var batch = new List<LogRecord>(first);

            // Gather whatever else is already waiting, so a busy moment becomes one COPY instead of many small ones.
            while (batch.Count < _maxBatchRecords && queue.TryRead(out var more))
            {
                batch.AddRange(more);
            }

            await WriteWithRetryAsync(batch, stoppingToken);
        }
    }

    private async Task WriteWithRetryAsync(List<LogRecord> batch, CancellationToken stoppingToken)
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

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Writing {Count} log records failed; retrying in {Delay}")]
    private partial void LogWriteFailed(Exception exception, int count, TimeSpan delay);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Veyra is stopping while {Count} log records are still unwritten; they are lost")]
    private partial void LogDroppedOnShutdown(int count);
}
