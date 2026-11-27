namespace Veyra.Alerting.Api;

/// <summary>
/// One catalog rule as the screens word its alerts (ADR 0020): its name, unit, and sentences, with the levels the
/// installation set.
/// </summary>
/// <param name="Id">The rule's name as alerts store it, such as <c>db-head-blocker</c>.</param>
/// <param name="Name">Its name on screens, such as "Head blocker".</param>
/// <param name="Description">What it watches.</param>
/// <param name="Unit">
/// <c>seconds</c>, <c>ratio</c>, <c>count</c>, <c>bytes</c>, <c>days</c>, or <c>flag</c>: how its values are shown.
/// </param>
/// <param name="Noun">For a count, what is counted, in the singular.</param>
/// <param name="Headline">What is wrong, with <c>{server}</c>, <c>{part}</c>, and <c>{value}</c> to fill in.</param>
/// <param name="Recovery">Where it stands once resolved, with the same placeholders.</param>
/// <param name="PartLabel">What a part of the server is for this rule, such as "Database", or null.</param>
/// <param name="LowerIsWorse">Whether a lower value is worse.</param>
/// <param name="Enabled">Whether it runs.</param>
/// <param name="Caution">The caution level, in its unit, or null for none.</param>
/// <param name="Warning">The warning level, in its unit.</param>
/// <param name="Meaning">
/// What its alert means and why it matters, with <c>{server}</c> and <c>{part}</c> to fill in (VEY-60).
/// </param>
/// <param name="Steps">What to do to resolve its alert, in order.</param>
public sealed record CatalogRuleResponse(
    string Id,
    string Name,
    string Description,
    string Unit,
    string? Noun,
    string Headline,
    string Recovery,
    string? PartLabel,
    bool LowerIsWorse,
    bool Enabled,
    double? Caution,
    double Warning,
    string Meaning,
    IReadOnlyList<FixStepResponse> Steps);
