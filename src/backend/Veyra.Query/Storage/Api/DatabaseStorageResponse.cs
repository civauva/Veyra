using Veyra.Query.Queries.Api;

namespace Veyra.Query.Storage.Api;

/// <summary>
/// One database in full (ADR 0020, areas 6 to 8): state and settings, files, log, backups and the log chain, integrity,
/// and what Veyra makes of them.
/// </summary>
/// <param name="Name">The database.</param>
/// <param name="State">Its state, such as <c>ONLINE</c>.</param>
/// <param name="RecoveryModel">Its recovery model, such as <c>FULL</c>.</param>
/// <param name="Compatibility">Its compatibility level.</param>
/// <param name="PageVerify">Its page verify option.</param>
/// <param name="AutoClose">Whether auto close is on.</param>
/// <param name="AutoShrink">Whether auto shrink is on.</param>
/// <param name="ReadCommittedSnapshot">Whether read committed snapshot is on.</param>
/// <param name="Encrypted">Whether it is encrypted.</param>
/// <param name="LogReuseWait">What its log waits for to be reused.</param>
/// <param name="LogUsedRatio">The share of its log in use, or <see langword="null"/>.</param>
/// <param name="VirtualLogFiles">Its virtual log files, or <see langword="null"/>.</param>
/// <param name="Files">Its files.</param>
/// <param name="FullBackupAgeSeconds">Time since its last full backup, or <see langword="null"/>.</param>
/// <param name="DiffBackupAgeSeconds">Time since its last differential backup, or <see langword="null"/>.</param>
/// <param name="LogBackupAgeSeconds">
/// Time since its last log backup, or <see langword="null"/> in simple recovery.
/// </param>
/// <param name="LogChainBroken">Whether its log backups have a gap since the last full backup.</param>
/// <param name="LastFullSeconds">How long its last full backup took, or <see langword="null"/>.</param>
/// <param name="LastFullBytes">How large it was, or <see langword="null"/>.</param>
/// <param name="LastFullDestination">Where it went, or <see langword="null"/>.</param>
/// <param name="CheckdbAgeSeconds">Time since its last good CHECKDB, or <see langword="null"/>.</param>
/// <param name="DamagedPages">Damaged pages not yet repaired.</param>
/// <param name="Findings">What Veyra makes of it, the worst first, with what to do.</param>
public sealed record DatabaseStorageResponse(
    string Name,
    string State,
    string RecoveryModel,
    string Compatibility,
    string PageVerify,
    bool AutoClose,
    bool AutoShrink,
    bool ReadCommittedSnapshot,
    bool Encrypted,
    string LogReuseWait,
    double? LogUsedRatio,
    double? VirtualLogFiles,
    IReadOnlyList<DatabaseFileResponse> Files,
    double? FullBackupAgeSeconds,
    double? DiffBackupAgeSeconds,
    double? LogBackupAgeSeconds,
    bool LogChainBroken,
    double? LastFullSeconds,
    double? LastFullBytes,
    string? LastFullDestination,
    double? CheckdbAgeSeconds,
    double DamagedPages,
    IReadOnlyList<PlanWarningResponse> Findings);
