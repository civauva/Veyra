using Veyra.Core.Agents;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// Drops probe answers. Stands in when no module runs probes, so ingestion runs on its own; the host's Agents module
/// replaces it (ADR 0028).
/// </summary>
internal sealed class NoAgentProbeReports : IAgentProbeReports
{
    /// <inheritdoc />
    public bool TryAccept(AgentProbeReport report) => true;
}
