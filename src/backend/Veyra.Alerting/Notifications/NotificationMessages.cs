using System.Globalization;
using System.Text;
using System.Text.Json;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Notifications;

/// <summary>
/// Words the notifications. One place for the text, so a webhook, an email, and a test say the same thing, in words
/// that fit each rule: "payments-gateway logs 6.2% errors", "disk / on web-01 is full in 9 days".
/// </summary>
internal static class NotificationMessages
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the message about one alert event.</summary>
    /// <param name="alertEvent">What changed.</param>
    /// <param name="alert">The alert as it is now.</param>
    /// <param name="publicUrl">
    /// Veyra's public address, for the link to the alert; <see langword="null"/> for none.
    /// </param>
    /// <returns>The message.</returns>
    public static NotificationMessage For(AlertEvent alertEvent, AlertState alert, Uri? publicUrl)
    {
        ArgumentNullException.ThrowIfNull(alertEvent);
        ArgumentNullException.ThrowIfNull(alert);

        var where = alert.Environment is null ? string.Empty : $" ({alert.Environment})";
        var severity = SeverityName(alertEvent.Severity);
        var state = $"{Headline(alert, alertEvent.Value)}{where}";
        var lasted = Duration(alertEvent.At - alert.OpenedAt);

        var (name, text) = alertEvent.Kind switch
        {
            AlertEventKind.Opened => ("opened", $"{Capitalise(severity)}: {state}"),
            AlertEventKind.Escalated => ("escalated", $"Now {severity}: {state}"),
            AlertEventKind.Eased => ("eased", $"Now {severity}: {state}"),
            _ => (
                "resolved",
                $"Resolved: {Recovery(alert, alertEvent.Value)}{where} after {lasted}"),
        };

        var subject = alert.Subject;
        var notice = new AlertNotice(
            Id: alert.Id,
            Rule: alert.Rule,
            Severity: severity,
            Service: subject.Kind == AlertBlockKind.Service ? subject.Name : null,
            Block: new BlockNotice(subject.KindName, subject.Name, subject.Part),
            Environment: alert.Environment,
            Unit: Unit(alert.Rule),
            Value: alertEvent.Value,
            Threshold: alert.Threshold,
            Peak: alert.Peak,
            WindowSeconds: (int)alert.Window.TotalSeconds,
            OpenedAt: alert.OpenedAt,
            ResolvedAt: alert.ResolvedAt,
            Url: publicUrl is null ? null : new Uri(publicUrl, $"alerts/{alert.Id}"));

        return new NotificationMessage(name, text, notice);
    }

    /// <summary>Builds the message an administrator sends to check a channel.</summary>
    /// <param name="channelName">The channel's name.</param>
    /// <returns>The message, with no alert.</returns>
    public static NotificationMessage Test(string channelName) =>
        new("test", $"Test from Veyra: notifications to {channelName} work.", null);

    /// <summary>Serialises a message as the JSON document webhooks receive.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The JSON, with camel-case names.</returns>
    public static string ToJson(NotificationMessage message) => JsonSerializer.Serialize(message, _json);

    /// <summary>Writes a message as a plain-text email body.</summary>
    /// <param name="message">The message.</param>
    /// <param name="channelName">The channel's name, so readers know where the email came from.</param>
    /// <returns>The body.</returns>
    public static string ToEmailBody(NotificationMessage message, string channelName)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = new StringBuilder(message.Text).Append("\n\n");

        if (message.Alert is { } alert)
        {
            Line(body, alert.Block.Kind == "host" ? "Host:" : "Service:", alert.Block.Name);

            if (alert.Block.Part is { } part)
            {
                Line(body, "Disk:", part);
            }

            Line(body, "Environment:", alert.Environment ?? "none named");
            Measurement(body, alert);
            Line(body, "Peak:", Measure(alert.Rule, alert.Peak));
            Line(body, "Opened:", Time(alert.OpenedAt));

            if (alert.ResolvedAt is { } resolvedAt)
            {
                Line(body, "Resolved:", Time(resolvedAt));
            }

            if (alert.Url is { } url)
            {
                Line(body, "Details:", url.ToString());
            }

            body.Append('\n');
        }

        body.Append(CultureInfo.InvariantCulture, $"Sent by Veyra to the channel \"{channelName}\". ")
            .Append("Administrators choose what it receives on the Notifications screen.\n");

        return body.ToString();
    }

    /// <summary>Formats a ratio as a percentage with at most one decimal, for example <c>6.2%</c>.</summary>
    /// <param name="ratio">The ratio from 0 to 1.</param>
    /// <returns>The percentage.</returns>
    public static string Percent(double ratio) =>
        (ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>Formats a measurement in its rule's unit: <c>6.2%</c>, <c>9 days</c>, or <c>420 ms</c>.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="value">The measurement.</param>
    /// <returns>The text.</returns>
    public static string Measure(string rule, double value) => rule switch
    {
        AlertRules.DiskFilling => Days(value),
        AlertRules.LatencyP95 => Seconds(value),
        _ => Percent(value),
    };

    // What is wrong, in a few words.
    private static string Headline(AlertState alert, double value) => alert.Rule switch
    {
        AlertRules.DiskSpace => $"{Disk(alert.Subject)} is {Percent(value)} full",
        AlertRules.DiskFilling => $"{Disk(alert.Subject)} is full in {Days(value)}",
        AlertRules.LatencyP95 => $"{alert.Subject.Name} answers slowly, p95 {Seconds(value)}",
        _ => $"{alert.Subject.Name} logs {Percent(value)} errors",
    };

    // Where it stands now that it resolved.
    private static string Recovery(AlertState alert, double value) => alert.Rule switch
    {
        AlertRules.DiskSpace => $"{Disk(alert.Subject)} is back to {Percent(value)} full",
        AlertRules.DiskFilling when value >= DiskFillingRule.NotFilling =>
            $"{Disk(alert.Subject)} is no longer filling",
        AlertRules.DiskFilling => $"{Disk(alert.Subject)} is full in {Days(value)} again",
        AlertRules.LatencyP95 => $"{alert.Subject.Name} is back to p95 {Seconds(value)}",
        _ => $"{alert.Subject.Name} is back to {Percent(value)} errors",
    };

    private static void Measurement(StringBuilder body, AlertNotice alert)
    {
        var window = Duration(TimeSpan.FromSeconds(alert.WindowSeconds));
        var from = $"{alert.Severity} from {Measure(alert.Rule, alert.Threshold)}";

        switch (alert.Rule)
        {
            case AlertRules.DiskSpace:
                Line(body, "In use:", $"{Percent(alert.Value)} ({from})");
                break;
            case AlertRules.DiskFilling:
                var full = alert.Value >= DiskFillingRule.NotFilling
                    ? "not filling"
                    : $"{Days(alert.Value)} at the pace of the last day";
                Line(body, "Full in:", $"{full} ({alert.Severity} within {Days(alert.Threshold)})");
                break;
            case AlertRules.LatencyP95:
                Line(body, "Latency:", $"p95 {Seconds(alert.Value)} over {window} ({from})");
                break;
            default:
                Line(body, "Error rate:", $"{Percent(alert.Value)} of records over {window} ({from})");
                break;
        }
    }

    private static string Unit(string rule) => rule switch
    {
        AlertRules.DiskFilling => "days",
        AlertRules.LatencyP95 => "seconds",
        _ => "ratio",
    };

    private static string Disk(AlertSubject subject) => $"disk {subject.Part ?? "?"} on {subject.Name}";

    // "9 days", "1 day", "less than a day".
    private static string Days(double days)
    {
        if (days < 0.5)
        {
            return "less than a day";
        }

        var whole = (int)Math.Round(days, MidpointRounding.AwayFromZero);

        return whole == 1 ? "1 day" : $"{whole} days";
    }

    // "420 ms" below a second, "3.4 s" from one.
    private static string Seconds(double seconds) =>
        seconds < 1
            ? (seconds * 1000).ToString("0", CultureInfo.InvariantCulture) + " ms"
            : seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";

    private static string SeverityName(AlertSeverity severity) =>
        severity == AlertSeverity.Warning ? "warning" : "caution";

    private static string Capitalise(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    // Readable lengths: "36 min", "1 h 36 min", "2 d 3 h".
    private static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return span.Hours == 0 ? $"{(int)span.TotalDays} d" : $"{(int)span.TotalDays} d {span.Hours} h";
        }

        if (span.TotalHours >= 1)
        {
            return span.Minutes == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalHours} h {span.Minutes} min";
        }

        return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min";
    }

    private static string Time(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static void Line(StringBuilder body, string label, string value) =>
        body.Append(label.PadRight(14)).Append(value).Append('\n');
}
