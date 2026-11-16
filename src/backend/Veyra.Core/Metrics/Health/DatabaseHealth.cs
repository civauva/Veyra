using Veyra.Core.Telemetry;

namespace Veyra.Core.Metrics.Health;

/// <summary>A database server as the built-in database rules judge it now (ADR 0018).</summary>
/// <param name="Server">The server's block, such as <c>microsoft.sql_server sql-prod-01</c>.</param>
/// <param name="Environment">The server's environment, or <see langword="null"/> when its metrics name none.</param>
/// <param name="BlockedLeast">
/// The fewest sessions blocked at any minute of the last five; <see langword="null"/> when the server sends none.
/// </param>
/// <param name="BlockedMost">The most sessions blocked at any minute of the last five.</param>
/// <param name="DeadlocksQuarter">
/// Deadlocks in the last 15 minutes; <see langword="null"/> when the server sends no deadlock rate.
/// </param>
/// <param name="DeadlocksHalfHour">Deadlocks in the last 30 minutes.</param>
/// <param name="Databases">Its databases, with their longest query and backup age.</param>
/// <param name="Jobs">Its SQL Agent jobs, with whether their last run failed.</param>
public sealed record DatabaseHealth(
    TelemetryBlock Server,
    string? Environment,
    double? BlockedLeast,
    double? BlockedMost,
    double? DeadlocksQuarter,
    double? DeadlocksHalfHour,
    IReadOnlyList<DatabasePartHealth> Databases,
    IReadOnlyList<AgentJobHealth> Jobs);
