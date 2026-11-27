using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veyra.Storage.Activity;
using Veyra.Storage.Ingest;
using Veyra.Storage.Jobs;
using Veyra.Storage.Logs;
using Veyra.Storage.Metrics;
using Veyra.Storage.Traces;

namespace Veyra.Storage;

/// <summary>
/// Keeps storage in shape while Veyra runs: daily partitions of logs, metric points, spans, database activity, job
/// runs, and ingest counts ahead of the clock, and old or excess data removed. Runs once at startup and then every
/// hour.
/// </summary>
/// <param name="logPartitions">Creates the daily log partitions.</param>
/// <param name="logRetention">Removes logs that are too old or over the size budget.</param>
/// <param name="metricPartitions">Creates the daily metric point partitions.</param>
/// <param name="metricRetention">Removes old metric points and series.</param>
/// <param name="spanPartitions">Creates the daily span partitions.</param>
/// <param name="traceRetention">Removes traces that are too old or over the size budget.</param>
/// <param name="activityPartitions">Creates the daily partitions of database activity.</param>
/// <param name="activityRetention">Removes old database activity.</param>
/// <param name="ingestPartitions">Creates the daily partitions of the ingest counts.</param>
/// <param name="ingestRetention">Removes ingest counts older than 30 days.</param>
/// <param name="jobPartitions">Creates the daily partitions of the job runs.</param>
/// <param name="jobRetention">Removes job runs older than their retention.</param>
/// <param name="clock">The clock behind the hourly timer.</param>
/// <param name="logger">Receives failures, which are retried on the next run.</param>
internal sealed partial class StorageMaintenance(
    LogPartitions logPartitions,
    LogRetention logRetention,
    MetricPartitions metricPartitions,
    MetricRetention metricRetention,
    SpanPartitions spanPartitions,
    TraceRetention traceRetention,
    ActivityPartitions activityPartitions,
    ActivityRetention activityRetention,
    IngestCountPartitions ingestPartitions,
    IngestCountRetention ingestRetention,
    JobRunPartitions jobPartitions,
    JobRunRetention jobRetention,
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
            // Logs, metrics, and traces are kept apart, so a failure in one does not hold up the others.
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

            try
            {
                await spanPartitions.EnsureAsync(stoppingToken);
                await traceRetention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, "Trace");
            }

            try
            {
                await activityPartitions.EnsureAsync(stoppingToken);
                await activityRetention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, "Database activity");
            }

            try
            {
                await ingestPartitions.EnsureAsync(stoppingToken);
                await ingestRetention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, "Ingest count");
            }

            try
            {
                await jobPartitions.EnsureAsync(stoppingToken);
                await jobRetention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, "Job run");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Signal} storage maintenance failed; retrying in an hour")]
    private partial void LogFailed(Exception exception, string signal);
}
