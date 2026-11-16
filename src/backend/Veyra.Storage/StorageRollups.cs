using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veyra.Storage.Logs;
using Veyra.Storage.Metrics;

namespace Veyra.Storage;

/// <summary>
/// Keeps the metric rollups current while Veyra runs (ADR 0015): once a minute, every finished 5 minutes and hour is
/// rolled up, and so is every hour that received late points. After an upgrade, the rest of each minute goes to
/// counting and rolling up the days written before, and to indexing their words by hour (ADR 0016), newest first.
/// </summary>
/// <param name="metricRollups">Rolls metric points up.</param>
/// <param name="logCountBackfill">Counts the log records written before counting existed.</param>
/// <param name="logWordIndexBackfill">Builds the index of words by hour on days written before it existed.</param>
/// <param name="clock">The clock behind the timer and the finished buckets.</param>
/// <param name="logger">Receives failures, which are retried on the next run.</param>
internal sealed partial class StorageRollups(
    MetricRollups metricRollups,
    LogCountBackfill logCountBackfill,
    LogWordIndexBackfill logWordIndexBackfill,
    TimeProvider clock,
    ILogger<StorageRollups> logger) : BackgroundService
{
    // Often enough that charts read at most a few minutes of points beyond the rollups.
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    // The backfill stops for the minute after this long, so the live rollups never fall far behind.
    private static readonly TimeSpan _backfillBudget = TimeSpan.FromSeconds(40);

    /// <summary>Rolls up now and then every minute until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, clock);

        do
        {
            try
            {
                await metricRollups.RollUpAsync(clock.GetUtcNow(), stoppingToken);
                await BackfillAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Reads take the points beyond the rollups, so a failed run only makes charts slower until the next.
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Counts and rolls up old days, one at a time, until none is left or the minute is nearly up.</summary>
    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var logs = true;
        var words = true;
        var metrics = true;

        // Log counts, word indexes, and metrics take turns, so each gets its newest days done first. Each stops once it
        // has none left.
        while ((logs || words || metrics) && clock.GetElapsedTime(started) < _backfillBudget)
        {
            if (logs)
            {
                logs = await logCountBackfill.CountNextDayAsync(cancellationToken);
            }

            if (words)
            {
                words = await logWordIndexBackfill.BuildNextDayAsync(cancellationToken);
            }

            if (metrics)
            {
                metrics = await metricRollups.BackfillNextDayAsync(cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollup or backfill failed; retrying in a minute")]
    private partial void LogFailed(Exception exception);
}
