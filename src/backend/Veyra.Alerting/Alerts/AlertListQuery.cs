using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>Which alerts a page of the Alerts screen shows.</summary>
/// <param name="From">The start of the period: alerts resolved before it are left out.</param>
/// <param name="To">The end of the period: alerts opened after it are left out.</param>
/// <param name="Limit">The most alerts on the page.</param>
/// <param name="Open">Only open alerts, only resolved ones, or, when <see langword="null"/>, both.</param>
/// <param name="Rules">Only alerts of these rules; <see langword="null"/> or empty for all.</param>
/// <param name="Severity">Only alerts whose worst severity was this one.</param>
/// <param name="Environment">Only alerts of this environment.</param>
/// <param name="Kind">Only alerts about services, or only about hosts.</param>
/// <param name="Block">Only alerts whose block name or part holds this text, in any case.</param>
/// <param name="After">Where the previous page ended; <see langword="null"/> for the first page.</param>
internal sealed record AlertListQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    bool? Open = null,
    IReadOnlyList<string>? Rules = null,
    AlertSeverity? Severity = null,
    string? Environment = null,
    AlertBlockKind? Kind = null,
    string? Block = null,
    AlertCursor? After = null);
