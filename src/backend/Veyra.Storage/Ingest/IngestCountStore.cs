using Npgsql;
using Veyra.Core.Telemetry;

namespace Veyra.Storage.Ingest;

/// <summary>Reads the ingest counts per step for the Overview (ADR 0023).</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class IngestCountStore(NpgsqlDataSource dataSource) : IIngestCounts
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestCount>> CountAsync(
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        TimeSpan bucket,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = IngestCounts.CreateCommand(environment, periodStart, periodEnd, bucket);
        command.Connection = connection;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var counts = new List<IngestCount>();

        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new IngestCount(
                reader.GetFieldValue<DateTimeOffset>(0),
                IngestCounts.SignalOf(reader.GetString(1)),
                reader.GetInt64(2)));
        }

        return counts;
    }
}
