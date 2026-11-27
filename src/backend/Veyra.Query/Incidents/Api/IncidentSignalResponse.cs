namespace Veyra.Query.Incidents.Api;

/// <summary>A moment from another signal on an incident's timeline (ADR 0026).</summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind"><c>job-failed</c>, <c>config-change</c>, <c>deadlock</c>, or <c>first-error</c>.</param>
/// <param name="Block">The block it happened on.</param>
/// <param name="Title">What happened, in words.</param>
/// <param name="Detail">More about it, such as the job's message or the setting's old and new value.</param>
public sealed record IncidentSignalResponse(
    DateTimeOffset At,
    string Kind,
    IncidentBlockResponse Block,
    string Title,
    string? Detail);
