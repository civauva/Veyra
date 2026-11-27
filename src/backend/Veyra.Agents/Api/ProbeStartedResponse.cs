namespace Veyra.Agents.Api;

/// <summary>A probe that was started; its outcome is read at <c>/api/v1/integrations/probes/{id}</c>.</summary>
/// <param name="Id">The probe's ID.</param>
public sealed record ProbeStartedResponse(Guid Id);
