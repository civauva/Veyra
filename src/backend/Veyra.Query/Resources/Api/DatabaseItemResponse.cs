using Veyra.Query.Home;

namespace Veyra.Query.Resources.Api;

/// <summary>
/// One database server of the list: every one whose metrics are kept, silent ones too (ADR 0022).
/// </summary>
/// <param name="Engine">The engine, as <c>db.system.name</c> spells it, such as <c>microsoft.sql_server</c>.</param>
/// <param name="Server">The server, as <c>server.address</c> names it.</param>
/// <param name="Version">
/// Its version, such as <c>SQL Server 2022 CU14</c>, from <c>sqlserver.server.info</c>; null where the engine does not
/// send it, as PostgreSQL's receiver does not.
/// </param>
/// <param name="Health">From its open alerts only (ADR 0007).</param>
/// <param name="Silent">Whether it sent nothing for longer than <c>Veyra:Query:SilentAfterHosts</c>.</param>
/// <param name="Databases">How many databases it has; its own are left out unless <c>system=true</c>.</param>
/// <param name="SizeBytes">Their files' size at the end of the window, added up.</param>
/// <param name="GrowthBytesPerDay">How much they grew per day at the pace of the last day.</param>
/// <param name="LastFullBackupAgeSeconds">
/// The longest any of them has gone without a full backup, tempdb aside; null where the engine does not send it.
/// </param>
/// <param name="LateBackups">
/// How many have no full backup within 26 hours; null where the engine does not send backups.
/// </param>
/// <param name="BlockingChains">
/// The blocking chains in its newest live read of the last minute of the window; null without a live read.
/// </param>
/// <param name="CpuRatio">
/// The CPU SQL Server itself used, averaged over the window, from 0 to 1; null where the engine does not send it.
/// </param>
public sealed record DatabaseItemResponse(
    string Engine,
    string Server,
    string? Version,
    BlockHealth Health,
    bool Silent,
    int Databases,
    double? SizeBytes,
    double? GrowthBytesPerDay,
    double? LastFullBackupAgeSeconds,
    int? LateBackups,
    int? BlockingChains,
    double? CpuRatio);
