using Veyra.Core.Activity;

namespace Veyra.Query.Queries.Api;

/// <summary>A statement's page (ADR 0020, area 4): its history over the range, its plans, and who runs it.</summary>
/// <param name="QueryHash">The statement's query hash.</param>
/// <param name="Database">The database it ran in, or <see langword="null"/>.</param>
/// <param name="Name">What it does, in words.</param>
/// <param name="Text">The statement.</param>
/// <param name="Range">The range.</param>
/// <param name="Totals">Its totals over the range, as on the Queries tab.</param>
/// <param name="Points">Each bucket of the range.</param>
/// <param name="Plans">The plans it used, the latest first.</param>
/// <param name="Findings">What its plans and history show, the worst first, with what to do.</param>
/// <param name="Callers">The programs and machines the live view saw run it.</param>
/// <param name="History">Its Query Store intervals over the range, oldest first; empty without Query Store.</param>
/// <param name="Waits">What it waited on over the range, the most first; empty without Query Store.</param>
public sealed record QueryResponse(
    string QueryHash,
    string? Database,
    string Name,
    string Text,
    string Range,
    QueryRowResponse Totals,
    IReadOnlyList<QueryPointResponse> Points,
    IReadOnlyList<QueryPlanResponse> Plans,
    IReadOnlyList<PlanWarningResponse> Findings,
    IReadOnlyList<SessionCaller> Callers,
    IReadOnlyList<QueryStoreIntervalResponse> History,
    IReadOnlyList<QueryWaitResponse> Waits)
{
    /// <summary>The start of the period the answer covers (ADR 0022).</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>The end of the period the answer covers: now, or the <c>to</c> asked for (ADR 0022).</summary>
    public DateTimeOffset To { get; init; }

    /// <summary>How many reads saw any program run it, the callers' shares added up on the server (VEY-68).</summary>
    public long CallerSamples => Callers.Sum(caller => caller.Samples);
}
