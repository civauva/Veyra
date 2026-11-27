namespace Veyra.Query.ServerOverview.Api;

/// <summary>A SQL Server instance and the machine it runs on, for the Overview's header (ADR 0025).</summary>
/// <param name="Version">The product version, such as <c>16.0.4135.4</c>.</param>
/// <param name="UpdateLevel">The cumulative update, such as <c>CU14</c>.</param>
/// <param name="Edition">The edition, such as <c>Enterprise Edition (64-bit)</c>.</param>
/// <param name="OsName">The operating system, such as <c>Windows Server 2022 Datacenter</c> or <c>Ubuntu</c>.</param>
/// <param name="OsRelease">The operating system's release, such as <c>10.0</c> or <c>22.04</c>.</param>
/// <param name="Cpus">The logical CPUs SQL Server sees.</param>
/// <param name="MemoryBytes">The machine's physical memory.</param>
/// <param name="StartedAt">When the instance started, worked out from its uptime.</param>
/// <param name="UptimeSeconds">Seconds since the instance started.</param>
/// <param name="Databases">The databases listed in the inventory.</param>
/// <param name="Platform">
/// <c>sqlserver</c>, <c>managed-instance</c>, or <c>azure-sql-database</c>, as the agent reports it; null before it
/// has.
/// </param>
public sealed record MachineResponse(
    string? Version,
    string? UpdateLevel,
    string? Edition,
    string? OsName,
    string? OsRelease,
    int? Cpus,
    long? MemoryBytes,
    DateTimeOffset? StartedAt,
    long? UptimeSeconds,
    int Databases,
    string? Platform);
