using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Works out the figures a database server's Overview tab tells beside its weather (VEY-62, VEY-68): the wait that
/// took the most time and its share, the databases without a recent full backup, the jobs that failed, and the
/// database oldest to transaction ID wraparound, so the browser only words them. Pure.
/// </summary>
internal static class DatabaseOverview
{
    /// <summary>A full backup older than this reads as late, as the backup-age rule's default caution.</summary>
    public static readonly TimeSpan LateBackup = TimeSpan.FromHours(26);

    /// <summary>Works out the figures of a server's answer.</summary>
    /// <param name="server">The server's answer, its lists as they will be sent.</param>
    /// <returns>The figures.</returns>
    public static DatabaseOverviewResponse Of(DatabaseMetricsResponse server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new DatabaseOverviewResponse(
            TopWait(server.Waits),
            [
                .. server.Databases
                    .Where(part => part.Name != "tempdb"
                        && (part.BackupAgeSeconds is not { } age || age >= LateBackup.TotalSeconds))
                    .Select(part => part.Name),
            ],
            [.. server.Jobs.Where(job => job.Failed)],
            server.Databases
                .Where(part => part.WraparoundRatio is not null)
                .OrderByDescending(part => part.WraparoundRatio)
                .FirstOrDefault());
    }

    // The first line is the wait that took the most time; its share is of the time all of them waited.
    private static WaitShareResponse? TopWait(IReadOnlyList<ChartLineResponse> waits)
    {
        var total = waits.Sum(Average);

        return waits.Count == 0 || total <= 0 ? null : new WaitShareResponse(waits[0].Label, Average(waits[0]) / total);
    }

    private static double Average(ChartLineResponse line)
    {
        var values = line.Points.Where(point => point.Value is not null).Select(point => point.Value!.Value).ToList();

        return values.Count == 0 ? 0 : values.Average();
    }
}
