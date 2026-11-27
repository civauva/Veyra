using Veyra.Agents.Probes;

namespace Veyra.Agents.Api;

/// <summary>A connection probe and its outcome (ADR 0028). Its password is never sent back.</summary>
/// <param name="Id">The probe's ID.</param>
/// <param name="State"><c>pending</c>, <c>succeeded</c>, or <c>failed</c>.</param>
/// <param name="StartedAt">When it was asked for.</param>
/// <param name="FinishedAt">When it finished; <see langword="null"/> while pending.</param>
/// <param name="Error">Why it failed, for a failed one.</param>
/// <param name="Result">What it read, for one that succeeded.</param>
public sealed record ProbeResponse(
    Guid Id,
    ProbeState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ProbeErrorResponse? Error,
    ProbeResult? Result);
