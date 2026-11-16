using Veyra.Query.Queries.Api;

namespace Veyra.Query.Server.Api;

/// <summary>A server's configuration and security posture (ADR 0020, area 13).</summary>
/// <param name="Settings">The settings worth knowing, with their values and what Veyra recommends.</param>
/// <param name="Findings">Settings off their recommended values and security findings, with their fix.</param>
/// <param name="Sysadmins">The members of the sysadmin role.</param>
/// <param name="Changes">Changes of settings and of sysadmin members in the last 30 days, the newest first.</param>
public sealed record ConfigurationResponse(
    IReadOnlyList<SettingResponse> Settings,
    IReadOnlyList<PlanWarningResponse> Findings,
    IReadOnlyList<string> Sysadmins,
    IReadOnlyList<ConfigurationChangeResponse> Changes);
