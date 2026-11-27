namespace Veyra.Agents.Probes;

/// <summary>What a probe read of a SQL Server (ADR 0028).</summary>
/// <param name="Version">The product version, such as <c>16.0.4135.4</c>.</param>
/// <param name="UpdateLevel">
/// The cumulative update, such as <c>CU14</c>, or the service level, such as <c>RTM</c>.
/// </param>
/// <param name="Edition">The edition, such as <c>Developer Edition (64-bit)</c>.</param>
/// <param name="Platform">
/// The kind of SQL Server the server says it is: <c>sqlserver</c>, <c>managed-instance</c>, or
/// <c>azure-sql-database</c>.
/// </param>
/// <param name="Databases">How many databases the login sees.</param>
/// <param name="DatabaseNames">The first 20 of them, by name.</param>
/// <param name="MissingPermissions">The permissions Veyra's reads need that the login lacks.</param>
public sealed record ProbeResult(
    string? Version,
    string? UpdateLevel,
    string? Edition,
    string? Platform,
    int Databases,
    IReadOnlyList<string> DatabaseNames,
    IReadOnlyList<string> MissingPermissions);
