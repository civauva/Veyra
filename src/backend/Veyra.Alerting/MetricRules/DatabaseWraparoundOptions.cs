namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in transaction ID wraparound rule for PostgreSQL databases, bound from the
/// <c>Veyra:Alerting:DatabaseWraparound</c> section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule judges the age of a database's oldest unfrozen transaction ID of the limit of about two billion. Near the
/// limit, PostgreSQL stops accepting writes until the database is vacuumed. Vacuuming freezes old rows and lowers the
/// age, so an open alert clears once the share falls below <see cref="ClearRatio"/>.
/// </remarks>
public sealed class DatabaseWraparoundOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The share of the limit, from 0 to 1, that raises a caution. Defaults to 0.50, 50 %.</summary>
    public double CautionRatio { get; set; } = 0.50;

    /// <summary>The share of the limit, from 0 to 1, that raises a warning. Defaults to 0.75, 75 %.</summary>
    public double WarningRatio { get; set; } = 0.75;

    /// <summary>
    /// The share of the limit, from 0 to 1, below which a caution ends. A warning ends as far below its own level.
    /// Defaults to 0.45, 45 %.
    /// </summary>
    public double ClearRatio { get; set; } = 0.45;
}
