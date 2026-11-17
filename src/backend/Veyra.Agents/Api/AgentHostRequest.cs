namespace Veyra.Agents.Api;

/// <summary>What an installed agent reads of the machine it runs on, as the Agents page sets it.</summary>
/// <param name="Host">Whether to read the host's CPU, memory, disks, and network.</param>
/// <param name="EventLog">Whether to read the Windows Event Log's errors; Windows only, and only with the host.</param>
/// <param name="Iis">Whether to read IIS; Windows only, and only with the host.</param>
/// <param name="Environment">The environment the host belongs to: letters, digits, dots, dashes, underscores.</param>
/// <param name="Docker">
/// Whether to read the Docker containers' stats and logs; Linux only, and only with the host. Left out, it is off.
/// </param>
public sealed record AgentHostRequest(bool Host, bool EventLog, bool Iis, string Environment, bool Docker = false);
