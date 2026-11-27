using Veyra.Query.Systems.Api;

namespace Veyra.Query.Metrics.Api;

/// <summary>A database server's screen (ADR 0018).</summary>
/// <param name="Card">The head of the screen.</param>
/// <param name="Range">The window, for example <c>6h</c>.</param>
/// <param name="BucketSeconds">The width of a point of the lines.</param>
/// <param name="Requests">
/// Batch requests per second; for PostgreSQL, transactions committed or rolled back per second.
/// </param>
/// <param name="Connections">User connections; for PostgreSQL, the connections to every database.</param>
/// <param name="Blocked">Sessions blocked.</param>
/// <param name="Deadlocks">Deadlocks per point.</param>
/// <param name="PageLife">Page life expectancy in seconds.</param>
/// <param name="LongestQuery">The longest running query in seconds, of any database.</param>
/// <param name="Waits">Seconds waited per second, one line per wait category, the largest first.</param>
/// <param name="Databases">The server's databases, the largest first.</param>
/// <param name="Jobs">SQL Agent jobs, the failed ones first.</param>
/// <param name="Running">Requests running for longer than ten seconds now, the longest first.</param>
/// <param name="TopQueries">
/// The statements with the most total time over the range, the agent's own left out.
/// </param>
public sealed record DatabaseMetricsResponse(
    DatabaseCardResponse Card,
    string Range,
    int BucketSeconds,
    ChartLineResponse Requests,
    ChartLineResponse Connections,
    ChartLineResponse Blocked,
    ChartLineResponse Deadlocks,
    ChartLineResponse PageLife,
    ChartLineResponse LongestQuery,
    IReadOnlyList<ChartLineResponse> Waits,
    IReadOnlyList<DatabasePartResponse> Databases,
    IReadOnlyList<AgentJobResponse> Jobs,
    IReadOnlyList<RunningQueryResponse> Running,
    IReadOnlyList<TopQueryResponse> TopQueries)
{
    /// <summary>The start of the period the answer covers (ADR 0022).</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>The end of the period the answer covers: now, or the <c>to</c> asked for (ADR 0022).</summary>
    public DateTimeOffset To { get; init; }

    /// <summary>
    /// The figures the Overview tab tells beside its weather, worked out from the lists as sent (VEY-68). The screen's
    /// builder always sets them.
    /// </summary>
    public DatabaseOverviewResponse? Overview { get; init; }

    /// <summary>What Veyra's agent itself asked of the server over the range; null when nothing.</summary>
    public AgentWorkResponse? AgentWork { get; init; }

    /// <summary>The server's maximum connections over the range; null where the engine does not say it.</summary>
    public ChartLineResponse? MaxConnections { get; init; }

    /// <summary>Committed transactions per second of every database (PostgreSQL); null for other engines.</summary>
    public ChartLineResponse? Commits { get; init; }

    /// <summary>Rolled back transactions per second of every database (PostgreSQL); null for other engines.</summary>
    public ChartLineResponse? Rollbacks { get; init; }

    /// <summary>The tables with the most dead rows now, the most first (PostgreSQL); empty for other engines.</summary>
    public IReadOnlyList<DatabaseTableResponse> Tables { get; init; } = [];

    /// <summary>
    /// The server's own databases, left out of <see cref="Databases"/> because they were not asked for.
    /// </summary>
    public HiddenRowsResponse HiddenDatabases { get; init; } = HiddenRowsResponse.None;

    /// <summary>The jobs SQL Server set up itself, left out of <see cref="Jobs"/>.</summary>
    public HiddenRowsResponse HiddenJobs { get; init; } = HiddenRowsResponse.None;

    /// <summary>The server's own statements, left out of <see cref="TopQueries"/>.</summary>
    public HiddenRowsResponse HiddenTopQueries { get; init; } = HiddenRowsResponse.None;

    /// <summary>
    /// The catalog's tables and those of the server's own databases, left out of <see cref="Tables"/>.
    /// </summary>
    public HiddenRowsResponse HiddenTables { get; init; } = HiddenRowsResponse.None;
}
