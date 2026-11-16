namespace Veyra.Core.Activity;

/// <summary>
/// The server-wide figures of one read of the live view. Every read sends one, even when no session is busy, so an
/// empty read is told from a missing one (ADR 0020, area 3).
/// </summary>
/// <param name="Block">The server's block.</param>
/// <param name="Environment">The server's environment.</param>
/// <param name="SampledAt">When the server was read.</param>
/// <param name="TempdbSizeKilobytes">The size of tempdb's data files.</param>
/// <param name="TempdbFreeKilobytes">The unallocated space in them.</param>
/// <param name="GrantsWaiting">Requests waiting for a memory grant.</param>
public sealed record ServerSample(
    string Block,
    string? Environment,
    DateTimeOffset SampledAt,
    long TempdbSizeKilobytes,
    long TempdbFreeKilobytes,
    int GrantsWaiting) : DatabaseActivity(Block, Environment, SampledAt);
