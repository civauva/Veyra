namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in backup-age rule for databases, bound from the <c>Veyra:Alerting:DatabaseBackupAge</c>
/// section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule raises an alert per database when its last full backup is too old, and clears with the next backup.
/// Databases that were never backed up send no age, so the rule leaves them alone, and so does it with
/// <see cref="IgnoredDatabases"/>.
/// </remarks>
public sealed class DatabaseBackupAgeOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours since the last full backup that raise a caution. Defaults to 26: a day and a little.</summary>
    public double CautionHours { get; set; } = 26;

    /// <summary>Hours since the last full backup that raise a warning. Defaults to 50: two nights missed.</summary>
    public double WarningHours { get; set; } = 50;

    /// <summary>
    /// Databases not backed up on purpose, by name, separated by commas, in any case; empty for none.
    /// </summary>
    public string? IgnoredDatabases { get; set; }
}
