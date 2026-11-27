using Veyra.Core.Activity;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics;

namespace Veyra.Query.ServerOverview;

/// <summary>
/// Everything a SQL Server's Overview is built from (ADR 0025), read at the same time, so the pure build can be tested
/// without a store.
/// </summary>
/// <param name="Window">The window of the lines, in the database screens' buckets.</param>
/// <param name="Lines">The series that draw lines, over the window and the equally long period before it.</param>
/// <param name="Recent">The last half hour in one-minute buckets, for the values now.</param>
/// <param name="Day">The database sizes over the last day, by hour.</param>
/// <param name="Month">The database sizes over the last 30 days, by day.</param>
/// <param name="Week">The volumes' free space over the last week, by day.</param>
/// <param name="Series">Every series found, for when each area last sent.</param>
/// <param name="Snapshot">The newest live read within a week, or <see langword="null"/>.</param>
/// <param name="EventSeen">When the newest event or deadlock arrived, or <see langword="null"/>.</param>
/// <param name="ConfigurationSeen">When the newest configuration record arrived, or <see langword="null"/>.</param>
internal sealed record ServerOverviewReads(
    MetricWindow Window,
    SeriesData Lines,
    SeriesData Recent,
    SeriesData Day,
    SeriesData Month,
    SeriesData Week,
    IReadOnlyList<MetricSeries> Series,
    SessionSnapshot? Snapshot,
    DateTimeOffset? EventSeen,
    DateTimeOffset? ConfigurationSeen);
