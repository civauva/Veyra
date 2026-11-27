using Veyra.Core.Jobs;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics;

namespace Veyra.Query.Jobs;

/// <summary>
/// Reads which SQL Server Agent jobs run at a moment, from the agent's <c>sqlserver.agent.job.running</c> metric
/// (ADR 0020, area 10): how long each has run, or a negative value for a job that does not run.
/// </summary>
/// <param name="search">The stored metrics.</param>
internal sealed class RunningJobs(IMetricSearch search)
{
    /// <summary>The metric that says how long a job has run so far.</summary>
    public const string Metric = "sqlserver.agent.job.running";

    // One series per job of every server.
    private const int _maxSeries = 10_000;

    // The agent reads its jobs every minute; a job that sent nothing for longer is not known to run.
    private static readonly TimeSpan _within = TimeSpan.FromMinutes(15);

    private readonly MetricReader _reader = new(search);

    /// <summary>The jobs that run at the end of a window.</summary>
    /// <param name="environment">
    /// Only servers of this environment and those whose agent names none; every environment when null.
    /// </param>
    /// <param name="end">The end of the window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The jobs that run, with how long they have run.</returns>
    public async Task<IReadOnlyList<RunningJob>> ReadAsync(
        string? environment,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(
                    end - _within,
                    Names: [Metric],
                    Environment: environment,
                    Limit: _maxSeries,
                    OrNoEnvironment: true),
                cancellationToken))
            .Where(one => one.Block?.Engine == SqlServerNames.Engine)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var recent = await _reader.ReadAsync(
            series,
            MetricWindow.Covering(end - _within, end, (int)_within.TotalMinutes),
            cancellationToken);

        return
        [
            .. series
                .GroupBy(one => (Block: one.Block!, one.Environment))
                .SelectMany(server => DatabaseView
                    .LatestBy(
                        recent.Where(one => one.Block == server.Key.Block && one.Environment == server.Key.Environment),
                        Metric,
                        SqlServerNames.JobAttribute)
                    .Where(pair => pair.Value >= 0)
                    .Select(pair => new RunningJob(
                        new JobKey(JobSource.SqlAgent, server.Key.Environment, server.Key.Block, pair.Key),
                        pair.Value))),
        ];
    }
}
