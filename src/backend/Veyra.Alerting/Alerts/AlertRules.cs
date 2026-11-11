namespace Veyra.Alerting.Alerts;

/// <summary>
/// The names of the built-in rules (ADR 0012), as they are stored with every alert and shown by the API.
/// </summary>
internal static class AlertRules
{
    /// <summary>The share of a service's log records that are errors.</summary>
    public const string ErrorRate = "error-rate";

    /// <summary>The share of a disk in use.</summary>
    public const string DiskSpace = "disk-space";

    /// <summary>The days until a disk is full at the pace of the last day.</summary>
    public const string DiskFilling = "disk-filling";

    /// <summary>The 95th percentile of a service's request durations.</summary>
    public const string LatencyP95 = "latency-p95";

    /// <summary>Every built-in rule, in the order screens list them.</summary>
    public static readonly IReadOnlyList<string> All = [ErrorRate, LatencyP95, DiskSpace, DiskFilling];

    /// <summary>
    /// Whether a lower measurement is worse. True only for days until full: the fewer, the sooner the disk is full.
    /// The worst measurement of such an alert, its peak, is its lowest.
    /// </summary>
    /// <param name="rule">The rule's name.</param>
    /// <returns><see langword="true"/> when lower is worse.</returns>
    public static bool LowerIsWorse(string rule) => rule == DiskFilling;
}
