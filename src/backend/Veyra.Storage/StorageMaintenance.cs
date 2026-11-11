using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veyra.Storage.Logs;
using Veyra.Storage.Metrics;

namespace Veyra.Storage;

/// <summary>
/// Keeps storage in shape while Veyra runs: daily partitions of logs and metric points ahead of the clock, and old or
/// excess data removed. Runs once at startup and then every hour.
/// </summary>
/// <param name="logPartitions">Creates the daily log partitions.</param>
/// <param name="logRetention">Removes logs that are too old or over the size budget.</param>
/// <param name="metricPartitions">Creates the daily metric point partitions.</param>
/// <param name="metricRetention">Removes old metric points and series.</param>
/// <param name="clock">The clock behind the hourly timer.</param>
/// <param name="logger">Receives failures, which are retried on the next run.</param>
internal sealed partial class StorageMaintenance(
    LogPartitions logPartitions,
    LogRetention logRetention,
    MetricPartitions metricPartitions,
    MetricRetention metricRetention,
    TimeProvider clock,
    ILogger<StorageMaintenance> logger) : BackgroundService
{
    // Hourly is far more often than daily partitions need, and keeps the size budget reasonably tight.
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    /// <summary>Runs maintenance now and then every hour until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, clock);

        do
        {
            // Logs and metrics are kept apart, so a failure in one does not hold up the other.
            try
            {
                await logPartitions.EnsureAsync(stoppingToken);
                await logRetention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A short database outage must not stop maintenance for good; the next run tries again.
                LogFailed(exception, "Log");
            }

            try
            {
                await metricPartitions.EnsureAsync(stoppingToken);
                await metricRetention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, "Metric");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Signal} storage maintenance failed; retrying in an hour")]
    private partial void LogFailed(Exception exception, string signal);
}
