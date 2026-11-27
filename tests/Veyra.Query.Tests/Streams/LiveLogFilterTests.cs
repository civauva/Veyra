using Veyra.Core.Telemetry;
using Veyra.Query.Streams;
using static Veyra.Query.Tests.Streams.LiveRecords;

namespace Veyra.Query.Tests.Streams;

public class LiveLogFilterTests
{
    [Fact]
    public void Matches_NoFilters_TakesEveryRecord()
    {
        var filter = Filter();

        Assert.True(filter.Matches(Record("anything")));
    }

    [Theory]
    [InlineData(LogSeverity.Error, true)]
    [InlineData(LogSeverity.Warning, true)]
    [InlineData(LogSeverity.Information, false)]
    public void Matches_Severity_TakesThatSeverityAndWorse(LogSeverity severity, bool expected)
    {
        var filter = Filter(severity: LogSeverity.Warning);

        Assert.Equal(expected, filter.Matches(Record("x", severity)));
    }

    [Fact]
    public void Matches_ServicesAndEnvironments_TakesOnlyThose()
    {
        var filter = Filter(services: ["payments", "orders"], environments: ["production"]);

        Assert.True(filter.Matches(Record("x", service: "orders")));
        Assert.False(filter.Matches(Record("x", service: "billing")));
        Assert.False(filter.Matches(Record("x", environment: "staging")));
        Assert.False(filter.Matches(Record("x", environment: null)));
    }

    [Theory]
    [InlineData("Payment DECLINED: card expired", true)]
    [InlineData("payment-declined", true)]
    [InlineData("payment accepted", false)]
    [InlineData("payments declined", false)]
    public void Matches_Text_NeedsEveryWordIgnoringCase(string body, bool expected)
    {
        var filter = Filter(text: "declined payment");

        Assert.Equal(expected, filter.Matches(Record(body)));
    }

    [Fact]
    public void Matches_TextWithoutWords_MatchesNothing()
    {
        var filter = Filter(text: "!!!");

        Assert.False(filter.Matches(Record("!!! alarm")));
    }

    [Fact]
    public void Matches_TraceSpanAndAttributes_MustAllAgree()
    {
        var filter = Filter(
            traceId: "4BF92F3577B34DA6A3CE929D0E0E4736",
            spanId: "00f067aa0ba902b7",
            attributes: new Dictionary<string, string> { ["http.route"] = "/pay" });
        var route = new Dictionary<string, string> { ["http.route"] = "/pay", ["other"] = "1" };

        Assert.True(filter.Matches(Record(
            "x",
            attributes: route,
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            spanId: "00f067aa0ba902b7")));
        Assert.False(filter.Matches(Record(
            "x",
            attributes: new Dictionary<string, string> { ["http.route"] = "/orders" },
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            spanId: "00f067aa0ba902b7")));
        Assert.False(filter.Matches(Record("x", attributes: route, traceId: "4bf92f3577b34da6a3ce929d0e0e4736")));
    }

    [Fact]
    public void Matches_HostsLevelsAndPhrases_TakeOnlyThose()
    {
        var filter = LiveLogFilter.From(Query() with
        {
            Hosts = ["web-1"],
            Levels = [LogSeverity.Warning, LogSeverity.Fatal],
            Text = "timed out",
            Phrases = ["Timed Out"],
        });

        Assert.True(filter.Matches(Record("request TIMED OUT", LogSeverity.Warning) with { HostName = "web-1" }));
        Assert.False(filter.Matches(Record("request timed out", LogSeverity.Error) with { HostName = "web-1" }));
        Assert.False(filter.Matches(Record("request timed out", LogSeverity.Warning) with { HostName = "web-2" }));
        Assert.False(filter.Matches(Record("request timed out", LogSeverity.Warning)));
        Assert.False(filter.Matches(Record("out: timed", LogSeverity.Warning) with { HostName = "web-1" }));
    }

    [Fact]
    public void Matches_NoLevels_MatchesNothing()
    {
        var filter = LiveLogFilter.From(Query() with { Levels = [] });

        Assert.False(filter.Matches(Record("x")));
    }

    private static LogQuery Query() =>
        new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), null, null, null, null, 100, null);

    private static LiveLogFilter Filter(
        LogSeverity? severity = null,
        string[]? services = null,
        string? text = null,
        string? traceId = null,
        string[]? environments = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        string? spanId = null) =>
        LiveLogFilter.From(new LogQuery(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(1),
            severity,
            services,
            text,
            traceId,
            100,
            null,
            environments,
            attributes,
            spanId));
}
