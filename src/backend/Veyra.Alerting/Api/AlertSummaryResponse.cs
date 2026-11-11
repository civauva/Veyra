namespace Veyra.Alerting.Api;

/// <summary>
/// What the Alerts screen counts over a period: open alerts, all alerts and warnings, how long they lasted, how many
/// each rule raised, and the worst alert of every day.
/// </summary>
/// <param name="From">The start of the period, at midnight UTC.</param>
/// <param name="To">The end of the period: now.</param>
/// <param name="Open">The alerts open now.</param>
/// <param name="OpenWarnings">The open alerts at warning.</param>
/// <param name="Total">Every alert open at some time in the period.</param>
/// <param name="Warnings">Those that were at warning at some time.</param>
/// <param name="Resolved">Those that resolved.</param>
/// <param name="MedianSeconds">
/// The median time from opening to resolving of those that resolved; <see langword="null"/> when none did.
/// </param>
/// <param name="Rules">How many alerts each built-in rule raised, every rule listed.</param>
/// <param name="Days">One entry per day of the period, oldest first.</param>
public sealed record AlertSummaryResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int Open,
    int OpenWarnings,
    int Total,
    int Warnings,
    int Resolved,
    double? MedianSeconds,
    IReadOnlyList<RuleCountResponse> Rules,
    IReadOnlyList<AlertDayResponse> Days);
