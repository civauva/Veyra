namespace Veyra.Query.Queries.Api;

/// <summary>A problem a plan or a statement's history shows, in words with what to do (ADR 0020, area 4).</summary>
/// <param name="Kind">
/// <c>implicit-conversion</c>, <c>spill</c>, <c>no-join-predicate</c>, <c>missing-index</c>, or <c>regression</c>.
/// </param>
/// <param name="Severity"><c>caution</c> or <c>warning</c>.</param>
/// <param name="Title">One line.</param>
/// <param name="Detail">Why it matters.</param>
/// <param name="Fix">What to do, in words.</param>
/// <param name="Script">A statement for a DBA to run, such as <c>CREATE INDEX</c>, or <see langword="null"/>.</param>
public sealed record PlanWarningResponse(
    string Kind,
    string Severity,
    string Title,
    string Detail,
    string Fix,
    string? Script);
