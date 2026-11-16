namespace Veyra.Query.Queries.Api;

/// <summary>A statement's plan in one Query Store interval (ADR 0020, area 4), over restarts and evictions.</summary>
/// <param name="Start">When the interval starts.</param>
/// <param name="End">When it ends.</param>
/// <param name="Plan">The plan hash.</param>
/// <param name="Runs">Runs in the interval.</param>
/// <param name="PerRunSeconds">Time per run.</param>
/// <param name="CpuPerRunSeconds">CPU per run.</param>
/// <param name="ReadsPerRun">Logical reads per run.</param>
/// <param name="TopWait">The wait category it waited on most, or empty.</param>
/// <param name="WaitSeconds">Its waits in the interval.</param>
public sealed record QueryStoreIntervalResponse(
    DateTimeOffset Start,
    DateTimeOffset End,
    string Plan,
    long Runs,
    double PerRunSeconds,
    double CpuPerRunSeconds,
    double ReadsPerRun,
    string TopWait,
    double WaitSeconds);
