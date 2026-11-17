using Veyra.Agents.PostgresServers;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;

namespace Veyra.Agents.SqlServers;

/// <summary>
/// Reads what tells a SQL Server's state from the stored telemetry: the agents' own reader errors and the servers'
/// latest figures. Both go through Core's search interfaces, so the Agents module never touches the Storage module.
/// </summary>
/// <param name="logs">Searches the stored logs, where the agents' own logs land.</param>
/// <param name="metrics">Finds the servers' metric series.</param>
internal sealed class SqlServerSignals(ILogSearch logs, IMetricSearch metrics)
{
    /// <summary>The service name the official Collector gives its own logs.</summary>
    public const string CollectorService = "otelcol-contrib";

    /// <summary>The <c>db.system.name</c> of SQL Server, which the template sets on everything it reads.</summary>
    public const string SqlServerEngine = "microsoft.sql_server";

    /// <summary>How recently a server's figures must have arrived for it to count as receiving.</summary>
    public static readonly TimeSpan DataWithin = TimeSpan.FromMinutes(5);

    // A few minutes of a handful of failing servers, each logging on every read, stays far below this.
    private const int _maxErrors = 500;

    /// <summary>Reads the reader errors the agents logged lately.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The errors of components that read a server Veyra set up; others are left out.</returns>
    public async Task<IReadOnlyList<ReaderError>> ReadErrorsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var page = await logs.SearchAsync(
            new LogQuery(
                From: now - SqlServerStatuses.ErrorsWithin,
                To: now,
                MinimumSeverity: LogSeverity.Error,
                Services: [CollectorService],
                Text: null,
                TraceId: null,
                Limit: _maxErrors,
                After: null,
                Attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["otelcol.component.kind"] = "receiver",
                }),
            cancellationToken);

        return page.Items
            .Select(item => item.Record)
            .Select(record => (
                Server: record.Attributes.TryGetValue("otelcol.component.id", out var component)
                    ? SqlServerStatuses.ServerOf(component)
                    : null,
                Record: record))
            .Where(entry => entry.Server is not null)
            .Select(entry => new ReaderError(
                entry.Server!.Value,
                entry.Record.Attributes.GetValueOrDefault("error") ?? entry.Record.Body,
                entry.Record.Timestamp))
            .ToList();
    }

    /// <summary>Finds when a server's figures last arrived.</summary>
    /// <param name="server">The server.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// The time, to the minute, or <see langword="null"/> when none arrived in the last five minutes.
    /// </returns>
    public Task<DateTimeOffset?> LastDataAsync(
        SqlServer server,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        return LastDataAsync(TelemetryBlock.Database(SqlServerEngine, server.Name), now, cancellationToken);
    }

    /// <summary>Finds when a PostgreSQL server's figures last arrived (VEY-33).</summary>
    /// <param name="server">The server.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>
    /// The time, to the minute, or <see langword="null"/> when none arrived in the last five minutes.
    /// </returns>
    public Task<DateTimeOffset?> LastDataAsync(
        PostgresServer server,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        return LastDataAsync(
            TelemetryBlock.Database(PostgresServerStatuses.Engine, server.Name),
            now,
            cancellationToken);
    }

    private async Task<DateTimeOffset?> LastDataAsync(
        TelemetryBlock block,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = await metrics.FindSeriesAsync(
            new MetricSeriesQuery(SeenSince: now - DataWithin, Limit: 1, Block: block),
            cancellationToken);

        return series.Count == 0 ? null : series.Max(one => one.LastSeen);
    }
}
