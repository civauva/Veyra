namespace Veyra.Query.Live.Api;

/// <summary>
/// One thing Veyra found on a database server, in words (ADR 0020): the Simple view leads with its title and detail,
/// the Advanced view adds its evidence in SQL Server's own terms.
/// </summary>
/// <param name="Kind">What kind of finding, such as <c>blocking</c> or <c>open-transaction</c>.</param>
/// <param name="Severity"><c>ok</c>, <c>caution</c>, or <c>warning</c>.</param>
/// <param name="Title">One sentence, for anyone.</param>
/// <param name="Detail">Why it matters and what usually causes it, for anyone.</param>
/// <param name="Advanced">The same in SQL Server's terms, for a DBA.</param>
/// <param name="Session">The session it is about, when it is about one.</param>
/// <param name="Actions">What can be done: statements to copy and places to look.</param>
public sealed record FindingResponse(
    string Kind,
    string Severity,
    string Title,
    string Detail,
    string Advanced,
    int? Session,
    IReadOnlyList<FindingActionResponse> Actions);
