namespace Veyra.Core.Settings;

/// <summary>
/// One setting as a module describes it: where it lives in configuration, how the Settings screen shows and edits it,
/// and what it defaults to (ADR 0017).
/// </summary>
/// <param name="Key">The configuration key, such as <c>Veyra:Storage:LogRetentionDays</c>.</param>
/// <param name="Section">The section of the Settings screen that shows it.</param>
/// <param name="Group">The card it sits on within its section, such as "Logs" or "Error rate".</param>
/// <param name="Label">A short name, such as "Keep".</param>
/// <param name="Help">What the setting does, in one or two plain sentences.</param>
/// <param name="Kind">The kind of value, which decides how it is edited.</param>
/// <param name="Default">
/// The value in code when nothing sets it, written as configuration writes it; null for none.
/// </param>
public sealed record SettingDefinition(
    string Key,
    SettingSection Section,
    string Group,
    string Label,
    string Help,
    SettingKind Kind,
    string? Default)
{
    /// <summary>The unit shown next to the value, such as "days", "s", or "%"; null for none.</summary>
    public string? Unit { get; init; }

    /// <summary>The lowest value allowed, in the unit shown; null for no lower limit.</summary>
    public double? Minimum { get; init; }

    /// <summary>The highest value allowed, in the unit shown; null for no upper limit.</summary>
    public double? Maximum { get; init; }

    /// <summary>The values a <see cref="SettingKind.Choice"/> can take; empty for every other kind.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>What changing it can do beyond itself.</summary>
    public SettingEffect Effect { get; init; } = SettingEffect.None;

    /// <summary>
    /// The variable in <c>deploy/.env</c> that gives its first value, such as <c>VEYRA_LOG_RETENTION_DAYS</c>; null
    /// when the Compose file passes none.
    /// </summary>
    public string? EnvironmentVariable { get; init; }

    /// <summary>
    /// Whether the setting shows only in the Settings screen's Advanced view. The Simple view keeps to the basics
    /// people change often.
    /// </summary>
    public bool Advanced { get; init; }

    /// <summary>
    /// Whether the setting may be left empty. Optional limits, such as a size budget, are empty until someone sets
    /// one.
    /// </summary>
    public bool Optional { get; init; }
}
