namespace Veyra.Alerting.Api;

/// <summary>The figures at the top of an incident's screen (ADR 0026).</summary>
/// <param name="FirstDetectedAt">When its first alert opened.</param>
/// <param name="DurationSeconds">How long it lasted, or has lasted so far while open.</param>
/// <param name="AffectedBlocks">How many blocks its alerts are about.</param>
/// <param name="Leading">The leading alert's measurement.</param>
public sealed record IncidentFiguresResponse(
    DateTimeOffset FirstDetectedAt,
    double DurationSeconds,
    int AffectedBlocks,
    IncidentLeadingResponse Leading);
