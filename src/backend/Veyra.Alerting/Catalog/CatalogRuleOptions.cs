namespace Veyra.Alerting.Catalog;

/// <summary>
/// The settings of one catalog rule, bound from <c>Veyra:Alerting:Catalog:{Setting}</c> (ADR 0020). A value left out
/// keeps the catalog's default.
/// </summary>
public sealed class CatalogRuleOptions
{
    /// <summary>Whether the rule runs; <see langword="null"/> for on.</summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// The caution level, in the Settings screen's unit for the rule; <see langword="null"/> for the default.
    /// </summary>
    public double? Caution { get; set; }

    /// <summary>
    /// The warning level, in the Settings screen's unit for the rule; <see langword="null"/> for the default.
    /// </summary>
    public double? Warning { get; set; }
}
