using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Api;

/// <summary>One integration, a monitored SQL Server with its template and modules (ADR 0028).</summary>
/// <param name="Id">The server's ID.</param>
/// <param name="Kind">The kind of integration; <c>sqlserver</c>.</param>
/// <param name="Name">The server's name in Veyra.</param>
/// <param name="Environment">The environment its telemetry is filed under.</param>
/// <param name="Agent">The agent that reads it.</param>
/// <param name="Version">The template version it is read with.</param>
/// <param name="LatestVersion">The current template version, which Update moves to.</param>
/// <param name="State">How its monitoring stands, in the words of the SQL Servers list.</param>
/// <param name="Modules">Each module's switch and interval, in the catalog's order.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="ChangedAt">When it was last changed.</param>
public sealed record IntegrationItemResponse(
    Guid Id,
    string Kind,
    string Name,
    string Environment,
    IntegrationAgentResponse Agent,
    string Version,
    string LatestVersion,
    SqlServerState State,
    IReadOnlyList<SqlServerModuleChoice> Modules,
    DateTimeOffset CreatedAt,
    DateTimeOffset ChangedAt);
