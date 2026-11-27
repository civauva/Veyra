using Veyra.Core.Alerts;
using Veyra.Query.Home;
using Veyra.Query.Metrics;
using Veyra.Query.Resources;

namespace Veyra.Query.Tests.Resources;

public class ResourceRankingTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 4, 20, TimeSpan.Zero);

    [Fact]
    public void WorstFirst_MixedHealth_WarningCautionSilentUnknownOkThenName()
    {
        var items = new[]
        {
            ("b-ok", BlockHealth.Ok, false),
            ("a-ok", BlockHealth.Ok, false),
            ("unknown", BlockHealth.Unknown, false),
            ("silent", BlockHealth.Ok, true),
            ("caution", BlockHealth.Caution, false),
            ("silent-warning", BlockHealth.Warning, true),
            ("Warning", BlockHealth.Warning, false),
        };

        var ranked = ResourceRanking.Sort(items, "worst", item => item.Item1, item => item.Item2, item => item.Item3);

        Assert.Equal(
            ["silent-warning", "Warning", "caution", "silent", "unknown", "a-ok", "b-ok"],
            ranked.Select(item => item.Item1));
    }

    [Fact]
    public void Sort_Name_IgnoresHealthAndCase()
    {
        var items = new[]
        {
            ("b", BlockHealth.Warning, false),
            ("A", BlockHealth.Ok, false),
            ("c", BlockHealth.Ok, true),
        };

        var ranked = ResourceRanking.Sort(items, "name", item => item.Item1, item => item.Item2, item => item.Item3);

        Assert.Equal(["A", "b", "c"], ranked.Select(item => item.Item1));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("worst", true)]
    [InlineData("NAME", true)]
    [InlineData("busiest", false)]
    public void IsSort_Values_AcceptsWorstAndName(string? sort, bool expected)
    {
        Assert.Equal(expected, ResourceRanking.IsSort(sort));
    }

    [Fact]
    public void ParseHealth_RepeatedAndCommaSeparated_ReadsEveryValueInAnyCase()
    {
        var filter = ResourceHealthFilter.Parse(["warning,Caution", "silent"]);

        Assert.NotNull(filter);
        Assert.True(filter.Keeps(BlockHealth.Warning, silent: false));
        Assert.True(filter.Keeps(BlockHealth.Caution, silent: false));
        Assert.True(filter.Keeps(BlockHealth.Ok, silent: true));
        Assert.False(filter.Keeps(BlockHealth.Ok, silent: false));
    }

    [Fact]
    public void ParseHealth_UnknownValue_IsRefused()
    {
        Assert.Null(ResourceHealthFilter.Parse(["red"]));
    }

    [Fact]
    public void ParseHealth_Nothing_KeepsEverything()
    {
        var filter = ResourceHealthFilter.Parse(null)!;

        Assert.True(filter.Keeps(BlockHealth.Ok, silent: false));
        Assert.True(filter.Keeps(BlockHealth.Warning, silent: true));
    }

    [Theory]
    [InlineData(112.0, 100.0, 0.12)]
    [InlineData(50.0, 100.0, -0.5)]
    public void Rate_CurrentAndPrevious_IsTheShareOfChange(double current, double previous, double expected)
    {
        Assert.Equal(expected, ResourceComparison.Rate(current, previous)!.Value, 6);
    }

    [Theory]
    [InlineData(10.0, null)]
    [InlineData(10.0, 0.0)]
    [InlineData(null, 10.0)]
    public void Rate_NoPreviousData_IsNull(double? current, double? previous)
    {
        Assert.Null(ResourceComparison.Rate(current, previous));
    }

    [Fact]
    public void Points_TwoRatios_IsTheirDifference()
    {
        Assert.Equal(0.0012, ResourceComparison.Points(0.0038, 0.0026)!.Value, 6);
        Assert.Null(ResourceComparison.Points(0.0038, null));
    }

    [Fact]
    public void Slices_MoreBucketsThanSlices_GiveRequestsPerMinuteOfEachSlice()
    {
        var window = MetricWindow.Card("24h", _now);
        var counts = window.Starts.ToDictionary(start => start, start => start >= window.Starts[12] ? 120.0 : 60.0);

        var slices = ResourceComparison.Slices(window, counts, 12);

        Assert.Equal(12, slices.Count);
        Assert.All(slices, slice => Assert.NotNull(slice));
        Assert.Equal(2.0, slices[^1]!.Value, 6);
    }

    [Fact]
    public void Slices_FewerBucketsThanSlices_RepeatTheBucketEachSliceFallsIn()
    {
        var window = MetricWindow.Card("15m", _now);
        var counts = window.Starts.ToDictionary(start => start, _ => 30.0);

        var slices = ResourceComparison.Slices(window, counts, 24);

        Assert.Equal(24, slices.Count);
        Assert.All(slices, slice => Assert.Equal(30.0, slice!.Value, 6));
    }

    [Fact]
    public void Slices_NoData_AreNull()
    {
        var window = MetricWindow.Card("1h", _now);

        var slices = ResourceComparison.Slices(window, new Dictionary<DateTimeOffset, double>(), 24);

        Assert.All(slices, slice => Assert.Null(slice));
    }

    [Fact]
    public void Page_CursorFromThePreviousPage_ContinuesWhereItStopped()
    {
        var items = Enumerable.Range(0, 5).ToList();

        var first = ResourcePage.Of(items, null, 2)!;
        var second = ResourcePage.Of(items, first.Next, 2)!;
        var last = ResourcePage.Of(items, second.Next, 2)!;

        Assert.Equal([0, 1], first.Items);
        Assert.Equal(5, first.Total);
        Assert.Equal([2, 3], second.Items);
        Assert.Equal([4], last.Items);
        Assert.Null(last.Next);
    }

    [Fact]
    public void Page_ForeignCursor_IsRefused()
    {
        Assert.Null(ResourcePage.Of([1, 2, 3], "not-a-cursor", 2));
    }

    [Fact]
    public void Health_OpenAlerts_TakeTheWorstSeverityOfTheBlock()
    {
        var health = new ResourceHealth(
        [
            Alert(AlertSubject.Host("web-01", "/var"), AlertSeverity.Caution, "production"),
            Alert(AlertSubject.Host("web-01", "/"), AlertSeverity.Warning, "production"),
            Alert(AlertSubject.Service("orders"), AlertSeverity.Caution, "staging"),
            Alert(AlertSubject.Container("api", "web-02"), AlertSeverity.Warning, null),
        ]);

        Assert.Equal(BlockHealth.Warning, health.Of(AlertBlockKind.Host, "web-01", null));
        Assert.Equal(BlockHealth.Ok, health.Of(AlertBlockKind.Service, "orders", "production"));
        Assert.Equal(BlockHealth.Caution, health.Of(AlertBlockKind.Service, "orders", "staging"));
        Assert.Equal(BlockHealth.Ok, health.Of(AlertBlockKind.Container, "api", null, "web-01"));
        Assert.Equal(BlockHealth.Warning, health.Of(AlertBlockKind.Container, "api", null, "web-02"));
    }

    private static AlertState Alert(AlertSubject subject, AlertSeverity severity, string? environment) => new(
        Guid.NewGuid(),
        "rule",
        environment,
        subject,
        severity,
        1,
        1,
        1,
        TimeSpan.FromMinutes(5),
        _now,
        _now,
        null);
}
