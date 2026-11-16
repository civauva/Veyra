namespace Veyra.Query.Queries.Api;

/// <summary>One statement of a SQL Server over the range, for the Queries tab (ADR 0020, area 4).</summary>
/// <param name="QueryHash">The statement's query hash, which opens its page.</param>
/// <param name="Database">The database it ran in, or <see langword="null"/>.</param>
/// <param name="Name">What it does, in words, such as "Update of Orders by CustomerId".</param>
/// <param name="Text">The statement, up to 1,000 characters.</param>
/// <param name="TotalSeconds">Time it took over the range, all runs together.</param>
/// <param name="Executions">Runs over the range.</param>
/// <param name="CpuSeconds">CPU time over the range.</param>
/// <param name="Reads">Logical reads over the range.</param>
/// <param name="PerRunSeconds">Time per run.</param>
/// <param name="Plans">How many plans it used over the range.</param>
/// <param name="Findings">What its plans and history show, the worst first.</param>
public sealed record QueryRowResponse(
    string QueryHash,
    string? Database,
    string Name,
    string Text,
    double TotalSeconds,
    long Executions,
    double CpuSeconds,
    long Reads,
    double PerRunSeconds,
    int Plans,
    IReadOnlyList<PlanWarningResponse> Findings);
