namespace Veyra.Core.Alerts;

/// <summary>
/// The backup rules' thresholds as an Admin last saved them on the Settings screen (ADR 0017). The Alerting module
/// owns the rules and implements it; the Query module judges backup freshness through it, so it never reads another
/// module's settings itself (ADR 0025).
/// </summary>
public interface IBackupThresholds
{
    /// <summary>The thresholds now; a saved change shows at the next call.</summary>
    BackupThresholds Current { get; }
}
