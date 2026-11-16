using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Settings;

/// <summary>
/// How much room a kind of data takes, by daily partition, so the Settings screen can say what a shorter retention or
/// a smaller budget would delete before anyone saves it (ADR 0017).
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="clock">Tells today, which retention counts back from.</param>
internal sealed class StorageFootprint(NpgsqlDataSource dataSource, TimeProvider clock)
{
    /// <summary>
    /// The days and bytes a retention of <paramref name="keepDays"/> would drop, as retention counts them.
    /// </summary>
    /// <param name="tables">The tables whose daily partitions make up a day of this data.</param>
    /// <param name="keepDays">The retention in days.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The number of days older than the first day kept, and their size.</returns>
    public async Task<(int Days, long Bytes)> OlderThanAsync(
        IReadOnlyList<string> tables,
        int keepDays,
        CancellationToken cancellationToken)
    {
        var firstDayKept = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-keepDays);
        var days = new HashSet<DateOnly>();
        long bytes = 0;

        foreach (var table in tables)
        {
            foreach (var (_, day, size) in await DailyPartitions.ListAsync(dataSource, table, cancellationToken))
            {
                if (day < firstDayKept)
                {
                    days.Add(day);
                    bytes += size;
                }
            }
        }

        return (days.Count, bytes);
    }

    /// <summary>The size of every daily partition of this data.</summary>
    /// <param name="tables">The tables whose daily partitions make up a day of this data.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The total size in bytes.</returns>
    public async Task<long> TotalAsync(IReadOnlyList<string> tables, CancellationToken cancellationToken)
    {
        long bytes = 0;

        foreach (var table in tables)
        {
            foreach (var (_, _, size) in await DailyPartitions.ListAsync(dataSource, table, cancellationToken))
            {
                bytes += size;
            }
        }

        return bytes;
    }
}
