namespace Veyra.Agents.Agents;

/// <summary>
/// What an agent reads of the machine it runs on, as an Admin set it under Settings → Agents (VEY-35, ADR 0018).
/// </summary>
/// <param name="Host">Whether to read the host's CPU, memory, disks, and network.</param>
/// <param name="EventLog">Whether to read the Windows Event Log's errors. Needs <paramref name="Host"/>.</param>
/// <param name="Iis">Whether to read IIS on Windows. Needs <paramref name="Host"/>.</param>
/// <param name="Environment">The environment the host belongs to, such as <c>production</c>.</param>
/// <param name="Docker">
/// Whether to read the Docker containers on Linux (VEY-32). Needs <paramref name="Host"/>: a container belongs to its
/// host.
/// </param>
internal sealed record HostWatch(bool Host, bool EventLog, bool Iis, string Environment, bool Docker = false)
{
    /// <summary>An agent that does not watch its host, which is how every agent starts.</summary>
    public static HostWatch None { get; } = new(false, false, false, "production");
}
