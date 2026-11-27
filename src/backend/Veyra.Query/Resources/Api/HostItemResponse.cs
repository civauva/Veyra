using Veyra.Query.Home;

namespace Veyra.Query.Resources.Api;

/// <summary>
/// One host or container of the list: every one whose metrics are kept, silent ones too, so a machine that stopped is
/// never missing (ADR 0022).
/// </summary>
/// <param name="Name">From <c>host.name</c>, or a container's <c>container.name</c>.</param>
/// <param name="Kind"><c>host</c> or <c>container</c>.</param>
/// <param name="Os">The operating system of a host, from <c>os.description</c> or <c>os.type</c>.</param>
/// <param name="Parent">The host a container runs on; null for a host.</param>
/// <param name="Health">From its open alerts only (ADR 0007).</param>
/// <param name="Silent">Whether it sent nothing for longer than <c>Veyra:Query:SilentAfterHosts</c>.</param>
/// <param name="CpuRatio">
/// CPU in use, averaged over the window, from 0 to 1; for a container, of one CPU, so above 1 when it uses more.
/// </param>
/// <param name="MemoryRatio">Memory in use at the end of the window; for a container, of its limit.</param>
/// <param name="FullestDiskRatio">The share in use of a host's fullest disk; null for a container.</param>
/// <param name="Runs">
/// Up to three services, database servers, and containers that run on a host, alphabetically.
/// </param>
/// <param name="RunsCount">How many run on it in all.</param>
/// <param name="LastSeenAt">When its metrics last arrived, to within a minute.</param>
public sealed record HostItemResponse(
    string Name,
    string Kind,
    string? Os,
    string? Parent,
    BlockHealth Health,
    bool Silent,
    double? CpuRatio,
    double? MemoryRatio,
    double? FullestDiskRatio,
    IReadOnlyList<string> Runs,
    int RunsCount,
    DateTimeOffset LastSeenAt);
