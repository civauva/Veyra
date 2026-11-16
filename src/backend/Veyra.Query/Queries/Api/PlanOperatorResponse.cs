namespace Veyra.Query.Queries.Api;

/// <summary>One operator of an execution plan, in plan order (ADR 0020, area 4).</summary>
/// <param name="Depth">How deep it sits in the plan's tree; 0 for the top.</param>
/// <param name="Physical">What it does, such as Index Scan or Key Lookup.</param>
/// <param name="Logical">Its logical operation, such as Inner Join.</param>
/// <param name="Target">The table and index it reads, or <see langword="null"/>.</param>
/// <param name="CostShare">Its own share of the plan's estimated cost, from 0 to 1.</param>
/// <param name="EstimatedRows">The rows SQL Server expected it to return.</param>
/// <param name="Warned">Whether the plan warns about it.</param>
public sealed record PlanOperatorResponse(
    int Depth,
    string Physical,
    string Logical,
    string? Target,
    double CostShare,
    double EstimatedRows,
    bool Warned);
