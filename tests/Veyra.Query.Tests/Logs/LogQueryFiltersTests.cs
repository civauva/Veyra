using Veyra.Core.Telemetry;
using Veyra.Query.Logs;

namespace Veyra.Query.Tests.Logs;

/// <summary>A query's terms become the filters the logs endpoints already take, ANDed with their parameters.</summary>
public class LogQueryFiltersTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_NoTerms_KeepsTheParameters()
    {
        var given = Given() with { Services = ["orders"], Text = "deadlock" };

        Assert.Same(given, Apply(given, ""));
    }

    [Fact]
    public void Apply_FieldTerms_BecomeTheirFilters()
    {
        var query = Apply(
            Given(),
            "service:orders service:\"Orders API\" env:production host:web-1 level:error level:fatal "
            + "trace:4BF92F3577B34DA6A3CE929D0E0E4736 span:00F067AA0BA902B7 http.route:/pay");

        Assert.Equal(["orders", "Orders API"], query.Services);
        Assert.Equal(["production"], query.Environments);
        Assert.Equal(["web-1"], query.Hosts);
        Assert.Equal([LogSeverity.Error, LogSeverity.Fatal], query.Levels);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", query.TraceId);
        Assert.Equal("00f067aa0ba902b7", query.SpanId);
        Assert.Equal("/pay", query.Attributes!["http.route"]);
        Assert.False(LogQueryFilters.MatchesNothing(query));
    }

    [Fact]
    public void Apply_WordsAndPhrases_AreAddedToTheText()
    {
        var query = Apply(Given() with { Text = "payment" }, "declined \"timed out\"");

        Assert.Equal("payment declined timed out", query.Text);
        Assert.Equal(["timed out"], query.Phrases);
    }

    [Fact]
    public void Apply_QuotedSingleWord_IsAWordNotAPhrase()
    {
        var query = Apply(Given(), "\"timeout\"");

        Assert.Equal("timeout", query.Text);
        Assert.Null(query.Phrases);
    }

    [Fact]
    public void Apply_LevelAtLeast_KeepsTheWorseOfTermAndParameter()
    {
        Assert.Equal(
            LogSeverity.Error,
            Apply(Given() with { MinimumSeverity = LogSeverity.Error }, "level>=warn").MinimumSeverity);
        Assert.Equal(
            LogSeverity.Warning,
            Apply(Given() with { MinimumSeverity = LogSeverity.Debug }, "level>=warn").MinimumSeverity);
    }

    [Fact]
    public void Apply_ServiceInBoth_KeepsOnlyServicesBothAllow()
    {
        var query = Apply(Given() with { Services = ["orders", "payments"] }, "service:payments service:billing");

        Assert.Equal(["payments"], query.Services);
        Assert.False(LogQueryFilters.MatchesNothing(query));
    }

    [Theory]
    [InlineData("service:billing")]
    [InlineData("env:staging")]
    [InlineData("host:web-2")]
    [InlineData("level:info")]
    [InlineData("trace:aaaa0000aaaa0000aaaa0000aaaa0000")]
    [InlineData("span:53995c3f42cd8ad8")]
    [InlineData("http.route:/order")]
    public void Apply_TermsThatExcludeTheParameters_MatchNothing(string text)
    {
        var given = Given() with
        {
            Services = ["orders"],
            Environments = ["production"],
            Hosts = ["web-1"],
            Levels = [LogSeverity.Error],
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            SpanId = "00f067aa0ba902b7",
            Attributes = new Dictionary<string, string> { ["http.route"] = "/pay" },
        };

        var query = Apply(given, text);

        Assert.True(LogQueryFilters.MatchesNothing(query));
    }

    [Theory]
    [InlineData("trace:aaaa0000aaaa0000aaaa0000aaaa0000 trace:bbbb0000bbbb0000bbbb0000bbbb0000")]
    [InlineData("tenant:a tenant:b")]
    public void Apply_TermsThatExcludeEachOther_MatchNothing(string text)
    {
        var query = Apply(Given(), text);

        Assert.True(LogQueryFilters.MatchesNothing(query));
    }

    [Fact]
    public void Apply_SameValueInBoth_StillMatches()
    {
        var given = Given() with
        {
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            Attributes = new Dictionary<string, string> { ["http.route"] = "/pay" },
        };

        var query = Apply(given, "trace:4BF92F3577B34DA6A3CE929D0E0E4736 http.route:/pay");

        Assert.False(LogQueryFilters.MatchesNothing(query));
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", query.TraceId);
    }

    private static LogQuery Given() => new(_now.AddHours(-1), _now, null, null, null, null, 100, null);

    private static LogQuery Apply(LogQuery given, string text)
    {
        var parse = LogQueryParser.Parse(text);

        Assert.Null(parse.Error);

        return LogQueryFilters.Apply(given, parse.Terms);
    }
}
