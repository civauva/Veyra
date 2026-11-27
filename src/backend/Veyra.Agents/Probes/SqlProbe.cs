using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Probes;

/// <summary>A connection probe, as stored without its password (ADR 0028).</summary>
/// <param name="Id">Its ID, which its Collector components are named after.</param>
/// <param name="AgentId">The agent that runs it; <see langword="null"/> when there was none.</param>
/// <param name="ServerId">The saved server it validates; <see langword="null"/> for a test before saving.</param>
/// <param name="Connection">The connection it tests.</param>
/// <param name="StartedAt">When it was asked for.</param>
/// <param name="FinishedAt">When it finished; <see langword="null"/> while it is pending.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Failure">Why it failed, for a failed one.</param>
/// <param name="Message">The error behind the failure, in words.</param>
/// <param name="Result">What it read, for one that succeeded.</param>
internal sealed record SqlProbe(
    Guid Id,
    Guid? AgentId,
    Guid? ServerId,
    SqlServerConnection Connection,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ProbeState State,
    ProbeFailure? Failure,
    string? Message,
    ProbeResult? Result);
