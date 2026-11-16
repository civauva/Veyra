using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Puts together a database server's screen (ADR 0018) from its metric series and its query events. It finds the
/// server's series through the block index (ADR 0015), then reads them once for the window and once for the last
/// five minutes, the deadlocks over the last 15 minutes, and the sizes over the last day.
/// </summary>
/// <param name="search">The stored metrics.</param>
/// <param name="events">The stored events of blocks.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class DatabaseViewBuilder(IMetricSearch search, IBlockEvents events, TimeProvider clock)
{
    // The query samples arrive every 15 seconds; this keeps the newest scrape even when one is late.
    private static readonly TimeSpan _samplesWithin = TimeSpan.FromSeconds(45);

    // The most events read of each kind: one scrape of a busy server.
    private const int _maxEvents = 500;

    // Four series per statement. The agent reports the busiest 50 statements a minute, so a week of a busy server stays
    // well below this.
    private const int _maxStatementSeries = 8_000;

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds a database server's screen.</summary>
    /// <param name="engine">
    /// The engine, as <c>db.system.name</c> spells it, such as <c>microsoft.sql_server</c>.
    /// </param>
    /// <param name="server">The server, as <c>server.address</c> names it.</param>
    /// <param name="range">The window, for example <c>6h</c>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The screen, or null when the server sent no metrics within the window.</returns>
    public async Task<DatabaseMetricsResponse?> DatabaseAsync(
        string engine,
        string server,
        string? range,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var window = MetricWindow.Of(range, now);
        var block = TelemetryBlock.Database(engine, server);
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(window.Start, Names: SqlServerNames.Screen, Block: block),
            cancellationToken);

        if (series.Count == 0)
        {
            return null;
        }

        // The reads do not depend on each other, so they run at the same time (ADR 0015).
        var readingWindow = _reader.ReadAsync(series, window, cancellationToken);
        var readingRecent = _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);
        var readingQuarter = _reader.ReadAsync(
            series.Where(one => one.Name == SqlServerNames.DeadlockRate).ToList(),
            Quarter(now),
            cancellationToken);
        var readingDay = _reader.ReadAsync(
            series.Where(one => one.Name == SqlServerNames.DatabaseSize).ToList(),
            MetricWindow.Day(now),
            cancellationToken);
        var listingSamples = events.ListAsync(
            block,
            SqlServerNames.QuerySample,
            now - _samplesWithin,
            now,
            _maxEvents,
            cancellationToken);
        var readingStatements = StatementsAsync(block, window, cancellationToken);

        await Task.WhenAll(readingWindow, readingRecent, readingQuarter, readingDay, listingSamples, readingStatements);

        return DatabaseView.Detail(
            block,
            await readingWindow,
            await readingRecent,
            await readingQuarter,
            await readingDay,
            await listingSamples,
            await readingStatements);
    }

    // Each statement's totals over the whole window, read as one bucket per series: one row per series, however long
    // the window, from the store's rollups (ADR 0015).
    private async Task<StatementTotals> StatementsAsync(
        TelemetryBlock block,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(
                window.Start,
                Names: SqlServerNames.Statements,
                Limit: _maxStatementSeries,
                Block: block),
            cancellationToken);

        if (series.Count == 0)
        {
            return new StatementTotals([], []);
        }

        var buckets = await search.ReadBucketsAsync(
            series.Select(one => one.Id).ToList(),
            window.Start,
            window.End,
            window.End - window.Start,
            cancellationToken);

        return new StatementTotals(series, buckets);
    }

    /// <summary>The last 15 minutes in one-minute buckets, for counting deadlocks.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Quarter(DateTimeOffset now) =>
        MetricWindow.Covering(now - TimeSpan.FromMinutes(15), now, 15);
}
