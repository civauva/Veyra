using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Api;

/// <summary>The integration package a server is read with: its template version and what it needs (ADR 0028).</summary>
/// <param name="Name">The package's name, <c>veyra-sqlserver-integration</c>.</param>
/// <param name="Version">The template version the server is read with.</param>
/// <param name="LatestVersion">The current template version, which Update moves to.</param>
/// <param name="PreviousVersion">
/// The version Roll back moves to; <see langword="null"/> when the server is already on it.
/// </param>
/// <param name="SetAt">When the version was set.</param>
/// <param name="SetBy">Who set it; <see langword="null"/> for Veyra itself.</param>
/// <param name="CollectorVersion">The version the agent's Collector reports, once it has.</param>
/// <param name="CollectorCompatibility">The Collector the template is tested with.</param>
/// <param name="MinimumSqlServer">The oldest SQL Server the template reads.</param>
/// <param name="Login">The login; <see langword="null"/> for integrated authentication.</param>
/// <param name="Authentication">How the agent signs in.</param>
/// <param name="LoginLacks">The permissions the login lacked at the agent's last check.</param>
public sealed record PackageResponse(
    string Name,
    string Version,
    string LatestVersion,
    string? PreviousVersion,
    DateTimeOffset SetAt,
    string? SetBy,
    string? CollectorVersion,
    string CollectorCompatibility,
    string MinimumSqlServer,
    string? Login,
    SqlServerAuthentication Authentication,
    IReadOnlyList<string> LoginLacks);
