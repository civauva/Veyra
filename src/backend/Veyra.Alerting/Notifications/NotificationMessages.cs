using System.Globalization;
using System.Text;
using System.Text.Json;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
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
    /// <param name="incident">
    /// The incident the alert belongs to, by its ID and number, so the message links it (ADR 0026); null for none.
    /// </param>
    /// <returns>The message.</returns>
    public static NotificationMessage For(
        AlertEvent alertEvent,
        AlertState alert,
        Uri? publicUrl,
        (Guid Id, long Number)? incident = null)
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

        // The alert keeps its own link, so receivers written before incidents keep working.
        var grouped = incident is { } member
            ? new IncidentNotice(
                member.Id,
                IncidentState.KeyOf(member.Number),
                publicUrl is null ? null : new Uri(publicUrl, $"incidents/{member.Id}"))
            : null;

        return new NotificationMessage(name, text, notice, grouped);
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
            var (blockLabel, partLabel) = alert.Block.Kind switch
            {
                "host" => ("Host:", "Disk:"),
                "database" => (
                    "Server:",
                    alert.Rule == AlertRules.DatabaseAgentJob
                        ? "Job:"
                        : RuleCatalog.Find(alert.Rule)?.PartLabel ?? "Database:"),
                "container" => ("Container:", "Host:"),
                _ => ("Service:", "Part:"),
            };
            var blockName = alert.Block.Kind == "database" ? Server(alert.Block.Name) : alert.Block.Name;
            Line(body, blockLabel, blockName);

            if (alert.Block.Part is { } part)
            {
                Line(body, partLabel, part);
            }

            Line(body, "Environment:", alert.Environment ?? "none named");
            Measurement(body, alert);
            Line(body, "Peak:", Measure(alert.Rule, alert.Peak));
            Line(body, "Opened:", Time(alert.OpenedAt));

            if (alert.ResolvedAt is { } resolvedAt)
            {
                Line(body, "Resolved:", Time(resolvedAt));
            }

            if (message.Incident is { } incident)
            {
                Line(body, "Incident:", incident.Url is { } link ? $"{incident.Key} {link}" : incident.Key);
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
        AlertRules.ContainerRestarts => Count(value, "restart"),
        AlertRules.DatabaseBlocking => Count(value, "session"),
        AlertRules.DatabaseDeadlocks => Count(value, "deadlock"),
        AlertRules.DatabaseLongQuery or AlertRules.DatabaseBackupAge => Duration(TimeSpan.FromSeconds(value)),
        AlertRules.DatabaseAgentJob => value >= 1 ? "failed" : "succeeded",
        _ when RuleCatalog.Find(rule) is { } catalog => CatalogWords.Value(catalog, value),
        _ => Percent(value),
    };

    /// <summary>
    /// Names a database server the way people say it: <c>SQL Server sql-prod-01</c> from
    /// <c>microsoft.sql_server sql-prod-01</c>.
    /// </summary>
    /// <param name="block">The server's block name: its engine and server.</param>
    /// <returns>The words.</returns>
    public static string Server(string block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var parts = block.Split(' ', 2);
        var engine = parts[0] switch
        {
            "microsoft.sql_server" => "SQL Server",
            "postgresql" => "PostgreSQL",
            _ => parts[0],
        };

        return parts.Length == 2 ? $"{engine} {parts[1]}" : engine;
    }

    /// <summary>
    /// What is wrong, in a few words, at the alert's latest measurement: "orders logs 6.2% errors". An incident is
    /// titled with it (ADR 0026), so its title reads as its notifications do.
    /// </summary>
    /// <param name="alert">The alert.</param>
    /// <returns>The headline, without the environment.</returns>
    public static string Headline(AlertState alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return Headline(alert, alert.Value);
    }

    // What is wrong, in a few words.
    private static string Headline(AlertState alert, double value) => alert.Rule switch
    {
        AlertRules.DiskSpace => $"{Disk(alert.Subject)} is {Percent(value)} full",
        AlertRules.DiskFilling => $"{Disk(alert.Subject)} is full in {Days(value)}",
        AlertRules.LatencyP95 => $"{alert.Subject.Name} answers slowly, p95 {Seconds(value)}",
        AlertRules.ContainerRestarts =>
            $"{Container(alert.Subject)} restarted {Count(value, "time")} in 15 minutes",
        AlertRules.ContainerMemory => $"{Container(alert.Subject)} uses {Percent(value)} of its memory limit",
        AlertRules.DatabaseBlocking =>
            $"{Server(alert.Subject.Name)}: {Count(value, "session")} blocked for 5 min",
        AlertRules.DatabaseDeadlocks =>
            $"{Server(alert.Subject.Name)}: {Count(value, "deadlock")} in 15 min",
        AlertRules.DatabaseLongQuery =>
            $"{Server(alert.Subject.Name)}: a query in {alert.Subject.Part} has run for {Measure(alert.Rule, value)}",
        AlertRules.DatabaseBackupAge =>
            $"{Server(alert.Subject.Name)}: {alert.Subject.Part} has no backup for {Measure(alert.Rule, value)}",
        AlertRules.DatabaseAgentJob =>
            $"{Server(alert.Subject.Name)}: SQL Agent job {alert.Subject.Part} failed",
        AlertRules.DatabaseConnections =>
            $"{Server(alert.Subject.Name)}: connections at {Percent(value)} of the maximum",
        AlertRules.DatabaseWraparound =>
            $"{Server(alert.Subject.Name)}: {alert.Subject.Part} is at {Percent(value)} of transaction ID wraparound",
        _ when RuleCatalog.Find(alert.Rule) is { } catalog => CatalogWords.Fill(
            catalog.Headline,
            Server(alert.Subject.Name),
            alert.Subject.Part,
            CatalogWords.Value(catalog, value)),
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
        AlertRules.ContainerRestarts => $"{Container(alert.Subject)} had no restart for 15 minutes",
        AlertRules.ContainerMemory =>
            $"{Container(alert.Subject)} is back to {Percent(value)} of its memory limit",
        AlertRules.DatabaseBlocking => $"{Server(alert.Subject.Name)} has no blocked sessions",
        AlertRules.DatabaseDeadlocks => $"{Server(alert.Subject.Name)} had no deadlock for 30 min",
        AlertRules.DatabaseLongQuery =>
            $"{Server(alert.Subject.Name)}: the long query in {alert.Subject.Part} ended",
        AlertRules.DatabaseBackupAge =>
            $"{Server(alert.Subject.Name)}: {alert.Subject.Part} has a new backup",
        AlertRules.DatabaseAgentJob =>
            $"{Server(alert.Subject.Name)}: SQL Agent job {alert.Subject.Part} succeeded",
        AlertRules.DatabaseConnections =>
            $"{Server(alert.Subject.Name)}: connections are back to {Percent(value)} of the maximum",
        AlertRules.DatabaseWraparound =>
            $"{Server(alert.Subject.Name)}: {alert.Subject.Part} is back to {Percent(value)} of transaction ID "
            + "wraparound",
        _ when RuleCatalog.Find(alert.Rule) is { } catalog => CatalogWords.Fill(
            catalog.Recovery,
            Server(alert.Subject.Name),
            alert.Subject.Part,
            CatalogWords.Value(catalog, value)),
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
            case AlertRules.ContainerRestarts:
                Line(body, "Restarts:", $"{Measure(alert.Rule, alert.Value)} in {window} ({from})");
                break;
            case AlertRules.ContainerMemory:
                Line(body, "Memory:", $"{Percent(alert.Value)} of its limit ({from})");
                break;
            case AlertRules.DatabaseBlocking:
                Line(body, "Blocked:", $"{Measure(alert.Rule, alert.Value)} throughout {window} ({from})");
                break;
            case AlertRules.DatabaseDeadlocks:
                Line(body, "Deadlocks:", $"{Measure(alert.Rule, alert.Value)} in {window} ({from})");
                break;
            case AlertRules.DatabaseLongQuery:
                Line(body, "Running for:", $"{Measure(alert.Rule, alert.Value)} ({from})");
                break;
            case AlertRules.DatabaseBackupAge:
                Line(body, "Last backup:", $"{Measure(alert.Rule, alert.Value)} ago ({from})");
                break;
            case AlertRules.DatabaseAgentJob:
                Line(body, "Last run:", Measure(alert.Rule, alert.Value));
                break;
            case AlertRules.DatabaseConnections:
                Line(body, "Connections:", $"{Percent(alert.Value)} of the maximum ({from})");
                break;
            case AlertRules.DatabaseWraparound:
                Line(body, "Wraparound:", $"{Percent(alert.Value)} of the transaction ID limit ({from})");
                break;
            case var rule when RuleCatalog.Find(rule) is { } catalog:
                Line(body, catalog.MeasurementLabel, $"{Measure(alert.Rule, alert.Value)} ({from})");
                break;
            default:
                Line(body, "Error rate:", $"{Percent(alert.Value)} of records over {window} ({from})");
                break;
        }
    }

    private static string Unit(string rule) => rule switch
    {
        AlertRules.DiskFilling => "days",
        AlertRules.LatencyP95 or AlertRules.DatabaseLongQuery or AlertRules.DatabaseBackupAge => "seconds",
        AlertRules.ContainerRestarts => "restarts",
        AlertRules.DatabaseBlocking => "sessions",
        AlertRules.DatabaseDeadlocks => "deadlocks",
        AlertRules.DatabaseAgentJob => "failed",
        _ when RuleCatalog.Find(rule) is { } catalog => catalog.Unit switch
        {
            MeasureUnit.Count => (catalog.Noun ?? "item") + "s",
            var unit => unit.ToString().ToLowerInvariant(),
        },
        _ => "ratio",
    };

    // "1 session", "3 sessions".
    private static string Count(double value, string noun)
    {
        var whole = (long)Math.Round(value, MidpointRounding.AwayFromZero);

        return whole == 1 ? $"1 {noun}" : $"{whole} {noun}s";
    }

    private static string Disk(AlertSubject subject) => $"disk {subject.Part ?? "?"} on {subject.Name}";

    // "Container shop-api on web-01"; the host is the subject's part (ADR 0018).
    private static string Container(AlertSubject subject) =>
        subject.Part is { } host ? $"Container {subject.Name} on {host}" : $"Container {subject.Name}";

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
