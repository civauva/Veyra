namespace Veyra.Core.Agents;

/// <summary>
/// Takes the answers of the connection probes Veyra's agents run (ADR 0028). The Ingestion module hands each
/// <c>sqlserver.probe</c> record it receives to it; the Agents module implements it, so neither references the other.
/// </summary>
/// <remarks>
/// Ingestion calls it while it answers an OTLP request, so it must not block: an implementation queues the report in
/// a bounded queue and refuses it when that is full, which Ingestion answers as a full queue, and the agent retries.
/// </remarks>
public interface IAgentProbeReports
{
    /// <summary>Queues one probe's answer.</summary>
    /// <param name="report">The answer, as the agent sent it.</param>
    /// <returns><see langword="false"/> when the queue is full; the sender should retry.</returns>
    bool TryAccept(AgentProbeReport report);
}
