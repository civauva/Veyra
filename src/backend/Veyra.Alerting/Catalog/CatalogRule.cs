namespace Veyra.Alerting.Catalog;

/// <summary>
/// One built-in rule of the catalog (ADR 0020): a measure the Query module reports for database servers, the levels at
/// which it raises a caution and a warning, and the words its alerts and notifications use.
/// </summary>
/// <remarks>
/// Catalog rules all judge alike: at or above the warning level a warning, at or above the caution level a caution,
/// below it nothing; lower is worse for a rule that says so. That covers most of what a DBA watches, so a new rule is
/// one entry here, not a class of its own with its own settings, words, and evaluation.
/// </remarks>
/// <param name="Id">The rule's name as alerts store it, such as <c>db-head-blocker</c>.</param>
/// <param name="Setting">
/// The section of its settings, such as <c>HeadBlocker</c> in <c>Veyra:Alerting:Catalog</c>.
/// </param>
/// <param name="Measure">The measure it judges, as the Query module names it.</param>
/// <param name="Name">Its name on screens, such as "Head blocker".</param>
/// <param name="Description">What it watches, for the Settings screen.</param>
/// <param name="Unit">The unit of the measure and thresholds.</param>
/// <param name="Noun">For <see cref="MeasureUnit.Count"/>, what is counted, in the singular.</param>
/// <param name="DefaultCaution">The caution level, in the measure's unit; <see langword="null"/> for none.</param>
/// <param name="DefaultWarning">The warning level, in the measure's unit.</param>
/// <param name="SettingUnit">The unit the Settings screen shows the levels in, such as "minutes".</param>
/// <param name="SettingScale">
/// How many of the measure's unit one setting unit is, such as 60 seconds a minute; 1 for a ratio, which the Settings
/// screen shows as a percentage by itself.
/// </param>
/// <param name="Headline">
/// What is wrong, with <c>{server}</c>, <c>{part}</c>, and <c>{value}</c> filled in, such as
/// "{server}: requests have waited {value} behind another session".
/// </param>
/// <param name="Recovery">Where it stands once resolved, with the same placeholders.</param>
/// <param name="MeasurementLabel">The label of the measurement in a notification, such as "Waited:".</param>
/// <param name="PartLabel">The label of the part in a notification, such as "Database:", for rules about parts.</param>
/// <param name="LowerIsWorse">Whether a lower value is worse.</param>
internal sealed record CatalogRule(
    string Id,
    string Setting,
    string Measure,
    string Name,
    string Description,
    MeasureUnit Unit,
    string? Noun,
    double? DefaultCaution,
    double DefaultWarning,
    string SettingUnit,
    double SettingScale,
    string Headline,
    string Recovery,
    string MeasurementLabel,
    string? PartLabel = null,
    bool LowerIsWorse = false);
