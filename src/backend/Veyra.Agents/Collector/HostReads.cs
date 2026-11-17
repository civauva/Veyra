namespace Veyra.Agents.Collector;

/// <summary>
/// What an agent reads of the host it runs on, when switched on under Settings → Agents (VEY-35, ADR 0018 and 0019):
/// the host's CPU, memory, disks, and network always; on Windows also the Event Log's errors and IIS; on Linux also
/// its Docker containers (VEY-32).
/// </summary>
/// <param name="Windows">Whether the host runs Windows; otherwise it is read as Linux.</param>
/// <param name="EventLog">Whether to read the System and Application logs' errors; Windows only.</param>
/// <param name="Iis">Whether to read IIS: its metrics and every site's requests from its W3C logs; Windows only.</param>
/// <param name="Environment">The environment the host belongs to, such as <c>production</c>.</param>
/// <param name="Docker">Whether to read the Docker containers' stats and logs; Linux only.</param>
internal sealed record HostReads(bool Windows, bool EventLog, bool Iis, string Environment, bool Docker = false);
