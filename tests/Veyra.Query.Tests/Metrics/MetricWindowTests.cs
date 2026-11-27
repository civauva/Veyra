using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

public class MetricWindowTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 4, 20, TimeSpan.Zero);

    [Theory]
    [InlineData(15, 60)]
    [InlineData(60, 60)]
    [InlineData(360, 300)]
    [InlineData(361, 3600)]
    [InlineData(1440, 3600)]
    [InlineData(10080, 3600)]
    [InlineData(43200, 21600)]
    public void Of_FromTo_StepIsMinutesUpToSixHoursAndHoursBeyond(int minutes, int bucketSeconds)
    {
        var span = new MetricSpan(null, _now.AddMinutes(-minutes), _now);

        var window = MetricWindow.Of(span, _now);

        Assert.Equal(TimeSpan.FromSeconds(bucketSeconds), window.Bucket);
        Assert.Equal("custom", window.Range);
        Assert.Equal((_now.AddMinutes(-minutes), _now), (window.From, window.End));
        Assert.Equal(0, window.Start.UtcTicks % window.Bucket.Ticks);
        Assert.True(window.Start - window.From < window.Bucket);
    }

    [Theory]
    [InlineData("1h", 60, 60)]
    [InlineData("6h", 300, 72)]
    [InlineData("24h", 3600, 24)]
    [InlineData("7d", 10800, 56)]
    [InlineData("30d", 43200, 60)]
    public void Card_Range_IsAWindowEndingNowWithTheSameStepRule(string range, int bucketSeconds, int points)
    {
        var window = MetricWindow.Card(range, _now);

        Assert.Equal((range, TimeSpan.FromSeconds(bucketSeconds)), (window.Range, window.Bucket));
        Assert.Equal(points, window.Starts.Count);
        Assert.Equal(_now, window.End);
    }

    [Fact]
    public void Of_PastWindow_EndsAtTo()
    {
        var to = _now.AddDays(-2);
        var span = new MetricSpan(null, to.AddHours(-1), to);

        var window = MetricWindow.Of(span, _now);

        Assert.Equal(to, window.End);
        Assert.Equal(60, window.Starts.Count);
    }

    [Fact]
    public void Resolve_OnlyTo_TakesTheRangeBeforeIt()
    {
        var to = _now.AddHours(-3);

        var resolved = new MetricSpan("1h", null, to).Resolve(_now);

        Assert.Equal((to.AddHours(-1), to, "custom"), (resolved!.From, resolved.To, resolved.Name));
    }

    [Fact]
    public void Resolve_OnlyFrom_EndsNow()
    {
        var resolved = new MetricSpan(null, _now.AddHours(-2), null).Resolve(_now);

        Assert.Equal((_now.AddHours(-2), _now), (resolved!.From, resolved.To));
    }

    [Fact]
    public void Resolve_UnknownRange_IsSixHours()
    {
        var resolved = MetricSpan.FromString("2w").Resolve(_now);

        Assert.Equal((_now.AddHours(-6), _now, "6h"), (resolved!.From, resolved.To, resolved.Name));
    }

    [Theory]
    [InlineData(31 * 24 * 60 + 1)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Resolve_LongerThanThirtyOneDaysOrNotPositive_IsRefused(int minutes)
    {
        var span = new MetricSpan(null, _now.AddMinutes(-minutes), _now);

        Assert.Null(span.Resolve(_now));
    }

    [Fact]
    public void Resolve_ExactlyThirtyOneDays_IsAccepted()
    {
        var span = new MetricSpan(null, _now.AddDays(-31), _now);

        Assert.NotNull(span.Resolve(_now));
    }
}
