using Veyra.Alerting.ErrorRates;
using Veyra.Alerting.MetricRules;
using Veyra.Alerting.Notifications;

namespace Veyra.Alerting;

/// <summary>
/// Settings of the Alerting module, bound from the <c>Veyra:Alerting</c> section.
/// </summary>
public sealed class AlertingOptions
{
    /// <summary>How often alert rules are evaluated against stored telemetry. Defaults to 30 seconds.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Veyra's public address, for example <c>https://veyra.example.com</c>. Notifications link to the alert with it;
    /// without it they carry no link. Docker Compose sets it from <c>VEYRA_DOMAIN</c>.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// How many days resolved alerts are kept for the Alerts screen. Defaults to 90 (ADR 0012); open alerts are never
    /// dropped.
    /// </summary>
    public int HistoryDays { get; set; } = 90;

    /// <summary>The built-in error-rate rule, bound from <c>Veyra:Alerting:ErrorRate</c>.</summary>
    public ErrorRateOptions ErrorRate { get; set; } = new();

    /// <summary>The built-in disk-space rule, bound from <c>Veyra:Alerting:DiskSpace</c>.</summary>
    public DiskSpaceOptions DiskSpace { get; set; } = new();

    /// <summary>The built-in disk-filling rule, bound from <c>Veyra:Alerting:DiskFilling</c>.</summary>
    public DiskFillingOptions DiskFilling { get; set; } = new();

    /// <summary>The built-in latency rule, bound from <c>Veyra:Alerting:Latency</c>.</summary>
    public LatencyOptions Latency { get; set; } = new();

    /// <summary>Alert notifications, bound from <c>Veyra:Alerting:Notifications</c>.</summary>
    public NotificationOptions Notifications { get; set; } = new();

    /// <summary>
    /// <see cref="PublicUrl"/> as an address that relative links resolve under, ending in a slash.
    /// </summary>
    /// <returns>The address, or <see langword="null"/> when none is set.</returns>
    public Uri? PublicBaseUri() =>
        string.IsNullOrWhiteSpace(PublicUrl)
            ? null
            : new Uri(PublicUrl.EndsWith('/') ? PublicUrl : PublicUrl + "/", UriKind.Absolute);
}
