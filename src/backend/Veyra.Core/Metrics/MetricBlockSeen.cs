using Veyra.Core.Telemetry;

namespace Veyra.Core.Metrics;

/// <summary>
/// A block that sent metrics, as the inventory knows it (ADR 0022): it stays known for as long as its series are kept,
/// so a host that stopped sending is still listed, as silent.
/// </summary>
/// <param name="Block">The block: a service, host, database server, or container.</param>
/// <param name="Environment">The environment its series name, or null when they name none.</param>
/// <param name="Host">
/// For a container, the host it runs on, from <c>host.name</c>: containers on two hosts may share a name (ADR 0018).
/// Null for every other kind.
/// </param>
/// <param name="Hosts">Every <c>host.name</c> its series name, alphabetically: where a service runs.</param>
/// <param name="Instances">
/// How many instances sent: distinct <c>service.instance.id</c>, or the host where a series names none.
/// </param>
/// <param name="ResourceAttributes">The resource attributes of its most recently seen series.</param>
/// <param name="FirstSeen">When its first point arrived.</param>
/// <param name="LastSeen">When a point last arrived, to within a minute.</param>
public sealed record MetricBlockSeen(
    TelemetryBlock Block,
    string? Environment,
    string? Host,
    IReadOnlyList<string> Hosts,
    int Instances,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);
