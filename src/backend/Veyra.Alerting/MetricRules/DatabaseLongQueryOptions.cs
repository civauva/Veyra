namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in long-query rule for databases, bound from the <c>Veyra:Alerting:DatabaseLongQuery</c>
/// section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule raises an alert per database when its longest running query has run for too long, and clears once no
/// query reached the caution level for five minutes.
/// </remarks>
public sealed class DatabaseLongQueryOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes a query has run that raise a caution. Defaults to 5.</summary>
    public double CautionMinutes { get; set; } = 5;

    /// <summary>Minutes a query has run that raise a warning. Defaults to 30.</summary>
    public double WarningMinutes { get; set; } = 30;
}
