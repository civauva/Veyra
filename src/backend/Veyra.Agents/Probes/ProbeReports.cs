using System.Threading.Channels;
using Veyra.Core.Agents;

namespace Veyra.Agents.Probes;

/// <summary>
/// The bounded queue between ingestion and the probe worker (ADR 0028). Ingestion hands it each probe answer while it
/// answers the agent's request; the worker finishes the probe. Probes are rare, so a small queue is ample, and a full
/// one makes the agent retry.
/// </summary>
internal sealed class ProbeReports : IAgentProbeReports
{
    /// <summary>How many answers may wait.</summary>
    public const int Capacity = 64;

    private readonly Channel<AgentProbeReport> _queue = Channel.CreateBounded<AgentProbeReport>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    /// <summary>Where the worker reads the answers.</summary>
    public ChannelReader<AgentProbeReport> Reader => _queue.Reader;

    /// <inheritdoc />
    public bool TryAccept(AgentProbeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return _queue.Writer.TryWrite(report);
    }
}
