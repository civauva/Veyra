namespace Veyra.Query.Overview.Api;

/// <summary>One step of the host trends, each a share from 0 to 1.</summary>
/// <param name="At">The start of the step.</param>
/// <param name="Cpu">The hosts' average CPU in use; null where no host measured the step.</param>
/// <param name="Memory">The hosts' average memory in use; null where no host measured the step.</param>
/// <param name="Disk">The fullest disk of any host; null where no host measured the step.</param>
public sealed record HostTrendPointResponse(DateTimeOffset At, double? Cpu, double? Memory, double? Disk);
