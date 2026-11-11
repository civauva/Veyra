using System.Text.Json;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Notifications;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.Notifications;

public class NotificationMessagesTests
{
    private static readonly DateTimeOffset _opened = new(2026, 11, 10, 6, 58, 0, TimeSpan.Zero);
    private static readonly Uri _site = new("https://veyra.example.com");

    [Fact]
    public void For_Opened_NamesSeverityServiceRatioAndEnvironment()
    {
        var message = NotificationMessages.For(Event(AlertEventKind.Opened, 0.0624), Alert(), _site);

        Assert.Equal("opened", message.Event);
        Assert.Equal("Warning: payments-gateway logs 6.2% errors (production)", message.Text);
        Assert.Equal(new Uri($"https://veyra.example.com/alerts/{Alert().Id}"), message.Alert!.Url);
    }

    [Fact]
    public void For_Escalated_SaysItIsNowWarning()
    {
        var message = NotificationMessages.For(Event(AlertEventKind.Escalated, 0.05), Alert(), _site);

        Assert.Equal("escalated", message.Event);
        Assert.Equal("Now warning: payments-gateway logs 5% errors (production)", message.Text);
    }

    [Fact]
    public void For_Resolved_SaysHowLongItLasted()
    {
        var resolvedAt = _opened.AddMinutes(96);
        var alert = Alert() with { ResolvedAt = resolvedAt, Value = 0.004 };
        var resolved = Event(AlertEventKind.Resolved, 0.004) with { At = resolvedAt };

        var message = NotificationMessages.For(resolved, alert, _site);

        Assert.Equal("resolved", message.Event);
        Assert.Equal("Resolved: payments-gateway is back to 0.4% errors (production) after 1 h 36 min", message.Text);
        Assert.Equal(resolvedAt, message.Alert!.ResolvedAt);
    }

    [Fact]
    public void For_NoEnvironmentAndNoPublicUrl_LeavesThemOut()
    {
        var message = NotificationMessages.For(
            Event(AlertEventKind.Opened, 0.02) with { Severity = AlertSeverity.Caution },
            Alert() with { Environment = null },
            null);

        Assert.Equal("Caution: payments-gateway logs 2% errors", message.Text);
        Assert.Null(message.Alert!.Url);
    }

    [Fact]
    public void ToJson_HasEventTextAndTheAlertInCamelCase()
    {
        var message = NotificationMessages.For(Event(AlertEventKind.Opened, 0.0624), Alert(), _site);

        using var json = JsonDocument.Parse(NotificationMessages.ToJson(message));
        var root = json.RootElement;

        Assert.Equal("opened", root.GetProperty("event").GetString());
        Assert.Equal(message.Text, root.GetProperty("text").GetString());
        var alert = root.GetProperty("alert");
        Assert.Equal("error-rate", alert.GetProperty("rule").GetString());
        Assert.Equal("warning", alert.GetProperty("severity").GetString());
        Assert.Equal("payments-gateway", alert.GetProperty("service").GetString());
        Assert.Equal("production", alert.GetProperty("environment").GetString());
        Assert.Equal(0.0624, alert.GetProperty("value").GetDouble());
        Assert.Equal(0.05, alert.GetProperty("threshold").GetDouble());
        Assert.Equal("2026-11-10T06:58:00+00:00", alert.GetProperty("openedAt").GetString());
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("resolvedAt").ValueKind);
        Assert.Equal($"https://veyra.example.com/alerts/{Alert().Id}", alert.GetProperty("url").GetString());
    }

    [Fact]
    public void Test_HasNoAlertAndNamesTheChannel()
    {
        var message = NotificationMessages.Test("#ops-alerts");

        Assert.Equal("test", message.Event);
        Assert.Equal("Test from Veyra: notifications to #ops-alerts work.", message.Text);
        Assert.Null(message.Alert);
        Assert.Contains("\"alert\":null", NotificationMessages.ToJson(message), StringComparison.Ordinal);
    }

    [Fact]
    public void ToEmailBody_ListsTheFactsAndTheLink()
    {
        var message = NotificationMessages.For(Event(AlertEventKind.Opened, 0.0624), Alert(), _site);

        var body = NotificationMessages.ToEmailBody(message, "On-call");

        Assert.StartsWith(message.Text + "\n\n", body, StringComparison.Ordinal);
        Assert.Contains("Error rate:   6.2% of records over 5 min (warning from 5%)", body, StringComparison.Ordinal);
        Assert.Contains("Opened:       2026-11-10 06:58 UTC", body, StringComparison.Ordinal);
        Assert.Contains($"Details:      https://veyra.example.com/alerts/{Alert().Id}", body, StringComparison.Ordinal);
        Assert.Contains("\"On-call\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void For_DiskFillingOpened_SaysWhenTheDiskIsFull()
    {
        var alert = Disk("disk-filling", value: 9.2, threshold: 14);

        var message = NotificationMessages.For(Event(AlertEventKind.Opened, 9.2) with { Severity = AlertSeverity.Caution }, alert, _site);

        Assert.Equal("Caution: disk / on web-01 is full in 9 days (production)", message.Text);
    }

    [Fact]
    public void For_DiskFillingResolvedNotFilling_SaysItStoppedFilling()
    {
        var resolvedAt = _opened.AddHours(5);
        var alert = Disk("disk-filling", value: 365, threshold: 14) with { ResolvedAt = resolvedAt };
        var resolved = Event(AlertEventKind.Resolved, 365) with { At = resolvedAt };

        var message = NotificationMessages.For(resolved, alert, _site);

        Assert.Equal("Resolved: disk / on web-01 is no longer filling (production) after 5 h", message.Text);
    }

    [Fact]
    public void For_DiskSpaceEscalated_SaysHowFullTheDiskIs()
    {
        var alert = Disk("disk-space", value: 0.961, threshold: 0.95);

        var message = NotificationMessages.For(Event(AlertEventKind.Escalated, 0.961), alert, _site);

        Assert.Equal("Now warning: disk / on web-01 is 96.1% full (production)", message.Text);
    }

    [Fact]
    public void For_LatencyOpenedAndResolved_NamesTheP95()
    {
        var alert = Alert() with { Rule = "latency-p95", Value = 3.4, Threshold = 3 };
        var resolvedAt = _opened.AddMinutes(25);

        var opened = NotificationMessages.For(Event(AlertEventKind.Opened, 3.4), alert, _site);
        var resolved = NotificationMessages.For(
            Event(AlertEventKind.Resolved, 0.42) with { At = resolvedAt },
            alert with { ResolvedAt = resolvedAt },
            _site);

        Assert.Equal("Warning: payments-gateway answers slowly, p95 3.4 s (production)", opened.Text);
        Assert.Equal("Resolved: payments-gateway is back to p95 420 ms (production) after 25 min", resolved.Text);
    }

    [Fact]
    public void ToJson_HostAlert_HasTheBlockAndNoService()
    {
        var message = NotificationMessages.For(Event(AlertEventKind.Opened, 9.2), Disk("disk-filling", 9.2, 14), _site);

        using var json = JsonDocument.Parse(NotificationMessages.ToJson(message));
        var alert = json.RootElement.GetProperty("alert");

        Assert.Equal(JsonValueKind.Null, alert.GetProperty("service").ValueKind);
        Assert.Equal("host", alert.GetProperty("block").GetProperty("kind").GetString());
        Assert.Equal("web-01", alert.GetProperty("block").GetProperty("name").GetString());
        Assert.Equal("/", alert.GetProperty("block").GetProperty("part").GetString());
        Assert.Equal("days", alert.GetProperty("unit").GetString());
    }

    [Fact]
    public void ToJson_ServiceAlert_KeepsTheServiceBesideTheBlock()
    {
        var message = NotificationMessages.For(Event(AlertEventKind.Opened, 0.0624), Alert(), _site);

        using var json = JsonDocument.Parse(NotificationMessages.ToJson(message));
        var alert = json.RootElement.GetProperty("alert");

        Assert.Equal("payments-gateway", alert.GetProperty("service").GetString());
        Assert.Equal("service", alert.GetProperty("block").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("block").GetProperty("part").ValueKind);
        Assert.Equal("ratio", alert.GetProperty("unit").GetString());
    }

    [Fact]
    public void ToEmailBody_DiskAlert_NamesTheHostTheDiskAndTheForecast()
    {
        var message = NotificationMessages.For(
            Event(AlertEventKind.Opened, 9.2) with { Severity = AlertSeverity.Caution },
            Disk("disk-filling", 9.2, 14) with { Severity = AlertSeverity.Caution },
            _site);

        var body = NotificationMessages.ToEmailBody(message, "On-call");

        Assert.Contains("Host:         web-01", body, StringComparison.Ordinal);
        Assert.Contains("Disk:         /", body, StringComparison.Ordinal);
        Assert.Contains("Full in:      9 days at the pace of the last day (caution within 14 days)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Service:", body, StringComparison.Ordinal);
    }

    private static AlertState Disk(string rule, double value, double threshold) => Alert() with
    {
        Rule = rule,
        Subject = AlertSubject.Host("web-01", "/"),
        Value = value,
        Peak = value,
        Threshold = threshold,
    };

    private static AlertEvent Event(AlertEventKind kind, double value) =>
        new(1, Alert().Id, kind, AlertSeverity.Warning, value, _opened);

    private static AlertState Alert() => new(
        Id: new Guid("0193a2b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b"),
        Rule: "error-rate",
        Environment: "production",
        Subject: AlertSubject.Service("payments-gateway"),
        Severity: AlertSeverity.Warning,
        Value: 0.216,
        Peak: 0.241,
        Threshold: 0.05,
        Window: TimeSpan.FromMinutes(5),
        OpenedAt: _opened,
        UpdatedAt: _opened.AddMinutes(36),
        ResolvedAt: null);
}
