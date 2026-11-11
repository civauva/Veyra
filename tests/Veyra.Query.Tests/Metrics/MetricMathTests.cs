using Veyra.Core.Metrics;
using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

public class MetricMathTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void Rates_CumulativeCounter_IsTheIncreasePerSecondBetweenBuckets()
    {
        var buckets = new[] { Counter(0, 100), Counter(1, 160), Counter(2, 280) };

        var rates = MetricMath.Rates(Series(MetricTemporality.Cumulative), buckets, _minute);

        Assert.Equal(2, rates.Count);
        Assert.Equal(1, rates[_noon.AddMinutes(1)]);
        Assert.Equal(2, rates[_noon.AddMinutes(2)]);
    }

    [Fact]
    public void Rates_CounterThatFell_CountsFromZeroAgainInsteadOfGoingNegative()
    {
        var buckets = new[] { Counter(0, 500), Counter(1, 30) };

        var rates = MetricMath.Rates(Series(MetricTemporality.Cumulative), buckets, _minute);

        Assert.Equal(0.5, rates[_noon.AddMinutes(1)]);
    }

    [Fact]
    public void Rates_NewStartTime_IsARestartEvenWhenTheValueRose()
    {
        var restarted = Counter(1, 900) with { LastStart = _noon.AddSeconds(30) };

        var rates = MetricMath.Rates(Series(MetricTemporality.Cumulative), [Counter(0, 600), restarted], _minute);

        Assert.Equal(15, rates[_noon.AddMinutes(1)]);
    }

    [Fact]
    public void Rates_DeltaSum_IsTheTotalOverTheBucket()
    {
        var bucket = Counter(0, 0) with { Total = 120 };

        Assert.Equal(2, MetricMath.Rates(Series(MetricTemporality.Delta), [bucket], _minute)[_noon]);
    }

    [Fact]
    public void Levels_AreTheAverages()
    {
        var levels = MetricMath.Levels([Counter(0, 1) with { Average = 4.5 }]);

        Assert.Equal(4.5, levels[_noon]);
    }

    [Fact]
    public void Increases_CumulativeHistogram_SubtractsTheBucketBefore()
    {
        var increases = MetricMath.Increases([Histogram(0, [10, 5, 1], 2), Histogram(1, [16, 9, 1], 3.5)]);

        var increase = Assert.Single(increases).Value;
        Assert.Equal([6L, 4, 0], increase.Counts);
        Assert.Equal(10, increase.Count);
        Assert.Equal(1.5, increase.Sum);
    }

    [Fact]
    public void Increases_HistogramThatRestarted_TakesItsWholeDistribution()
    {
        var increases = MetricMath.Increases([Histogram(0, [10, 5, 1], 2), Histogram(1, [2, 1, 0], 0.3)]);

        Assert.Equal([2L, 1, 0], Assert.Single(increases).Value.Counts);
    }

    [Fact]
    public void Quantile_InterpolatesInsideTheBucketThatHoldsIt()
    {
        var distribution = new Distribution([0.1, 1], [6, 3, 1], null);

        Assert.Equal(0.05, distribution.Quantile(0.3)!.Value, 6);
        Assert.Equal(0.55, distribution.Quantile(0.75)!.Value, 6);
    }

    [Fact]
    public void Quantile_InTheOpenLastBucket_IsTheHighestBound()
    {
        Assert.Equal(1, new Distribution([0.1, 1], [6, 3, 1], null).Quantile(0.99));
    }

    [Fact]
    public void Quantile_WithoutValues_IsNull()
    {
        Assert.Null(new Distribution([0.1, 1], [0, 0, 0], null).Quantile(0.5));
    }

    [Fact]
    public void Combine_AddsSeriesWithTheSameBoundsAndSkipsOthers()
    {
        var a = new Dictionary<DateTimeOffset, Distribution> { [_noon] = new([1], [1, 2], 1) };
        var b = new Dictionary<DateTimeOffset, Distribution> { [_noon] = new([1], [3, 4], 2) };
        var other = new Dictionary<DateTimeOffset, Distribution> { [_noon] = new([5], [9, 9], 9) };

        var combined = MetricMath.Combine([a, b, other])[_noon];

        Assert.Equal([4L, 6], combined.Counts);
        Assert.Equal(3, combined.Sum);
    }

    private static MetricSeries Series(MetricTemporality temporality) => new(
        1,
        "requests",
        MetricKind.Sum,
        "{request}",
        string.Empty,
        temporality,
        true,
        "api",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        _noon,
        _noon);

    private static MetricBucket Counter(int minute, double value) => new(
        1,
        _noon.AddMinutes(minute),
        1,
        value,
        value,
        _noon.AddMinutes(minute).AddSeconds(30),
        _noon.AddHours(-1),
        value,
        null);

    private static MetricBucket Histogram(int minute, long[] counts, double sum) => new(
        1,
        _noon.AddMinutes(minute),
        1,
        null,
        null,
        _noon.AddMinutes(minute).AddSeconds(30),
        _noon.AddHours(-1),
        null,
        new HistogramValue(counts.Sum(), sum, null, null, [0.1, 1], counts));
}
