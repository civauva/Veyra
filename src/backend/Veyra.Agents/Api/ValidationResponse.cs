namespace Veyra.Agents.Api;

/// <summary>The integration's last validation: its latest probe of the last day (ADR 0028).</summary>
/// <param name="LastProbe">The probe, or <see langword="null"/> when there was none lately.</param>
public sealed record ValidationResponse(ProbeResponse? LastProbe);
