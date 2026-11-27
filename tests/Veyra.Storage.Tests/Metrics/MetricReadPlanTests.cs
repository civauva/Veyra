using Veyra.Storage.Metrics;

namespace Veyra.Storage.Tests.Metrics;

public class MetricReadPlanTests
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);

    [Fact]
    public void Split_HourlyBuckets_ReadHoursThenFiveMinutesThenThePointsNotRolledUpYet()
    {
        var now = _day.AddHours(14).AddMinutes(7).AddSeconds(3);

        var segments = MetricReadPlan.Split(
            _day,
            now,
            _hour,
            fiveMinutesUpTo: _day.AddHours(14).AddMinutes(5),
            hoursUpTo: _day.AddHours(14),
            unrolled: []);

        Assert.Equal(
            [
                new MetricReadSegment(_day, _day.AddHours(14), MetricReadSource.Hours),
                new MetricReadSegment(_day.AddHours(14), _day.AddHours(14).AddMinutes(5), MetricReadSource.FiveMinutes),
                new MetricReadSegment(_day.AddHours(14).AddMinutes(5), now, MetricReadSource.Points),
            ],
            segments);
    }

    [Fact]
    public void Split_FifteenMinuteBuckets_ReadFiveMinutesOnly()
    {
        var segments = MetricReadPlan.Split(
            _day,
            _day.AddHours(3),
            TimeSpan.FromMinutes(15),
            fiveMinutesUpTo: _day.AddHours(5),
            hoursUpTo: _day.AddHours(5),
            unrolled: []);

        Assert.Equal([new MetricReadSegment(_day, _day.AddHours(3), MetricReadSource.FiveMinutes)], segments);
    }

    [Fact]
    public void Split_MinuteBuckets_ReadThePoints()
    {
        var segments = MetricReadPlan.Split(
            _day,
            _day.AddHours(1),
            TimeSpan.FromMinutes(1),
            fiveMinutesUpTo: _day.AddHours(5),
            hoursUpTo: _day.AddHours(5),
            unrolled: []);

        Assert.Equal([new MetricReadSegment(_day, _day.AddHours(1), MetricReadSource.Points)], segments);
    }

    [Fact]
    public void Split_OneBucketOverAWeekEndingNow_ReadsTheRollups()
    {
        // A total over the 7-day range, such as a statement's, is one bucket from an hour boundary to now: every rollup
        // row inside it falls into that one bucket, whatever its odd width (VEY-65).
        var from = _day.AddDays(-7).AddHours(15);
        var now = _day.AddHours(14).AddMinutes(7).AddSeconds(3);

        var segments = MetricReadPlan.Split(
            from,
            now,
            now - from,
            fiveMinutesUpTo: _day.AddHours(14).AddMinutes(5),
            hoursUpTo: _day.AddHours(14),
            unrolled: []);

        Assert.Equal(
            [
                new MetricReadSegment(from, _day.AddHours(14), MetricReadSource.Hours),
                new MetricReadSegment(_day.AddHours(14), _day.AddHours(14).AddMinutes(5), MetricReadSource.FiveMinutes),
                new MetricReadSegment(_day.AddHours(14).AddMinutes(5), now, MetricReadSource.Points),
            ],
            segments);
    }

    [Fact]
    public void Split_OneBucketFromAFiveMinuteBoundary_ReadsFiveMinutesThenThePoints()
    {
        var from = _day.AddHours(8).AddMinutes(10);
        var now = _day.AddHours(14).AddMinutes(7);

        var segments = MetricReadPlan.Split(
            from,
            now,
            now - from,
            fiveMinutesUpTo: _day.AddHours(14).AddMinutes(5),
            hoursUpTo: _day.AddHours(14),
            unrolled: []);

        Assert.Equal(
            [
                new MetricReadSegment(from, _day.AddHours(14).AddMinutes(5), MetricReadSource.FiveMinutes),
                new MetricReadSegment(_day.AddHours(14).AddMinutes(5), now, MetricReadSource.Points),
            ],
            segments);
    }

    [Fact]
    public void Split_OneBucketOffAFiveMinuteBoundary_ReadsThePoints()
    {
        var from = _day.AddHours(8).AddMinutes(7);
        var now = _day.AddHours(14).AddMinutes(7);

        var segments = MetricReadPlan.Split(
            from,
            now,
            now - from,
            fiveMinutesUpTo: _day.AddHours(14).AddMinutes(5),
            hoursUpTo: _day.AddHours(14),
            unrolled: []);

        Assert.Equal([new MetricReadSegment(from, now, MetricReadSource.Points)], segments);
    }

    [Fact]
    public void Split_WindowNotOnABucketBoundary_ReadsThePoints()
    {
        var from = _day.AddMinutes(7);

        var segments = MetricReadPlan.Split(
            from,
            _day.AddHours(3),
            _hour,
            fiveMinutesUpTo: _day.AddHours(5),
            hoursUpTo: _day.AddHours(5),
            unrolled: []);

        Assert.Equal([new MetricReadSegment(from, _day.AddHours(3), MetricReadSource.Points)], segments);
    }

    [Fact]
    public void Split_EndInsideAnHourBeforeTheRollups_ReadsFiveMinutesAndPointsForTheLastHour()
    {
        // An alert's chart can end at any time in the past, not only now.
        var to = _day.AddHours(3).AddMinutes(12);

        var segments = MetricReadPlan.Split(
            _day,
            to,
            _hour,
            fiveMinutesUpTo: _day.AddHours(5),
            hoursUpTo: _day.AddHours(5),
            unrolled: []);

        Assert.Equal(
            [
                new MetricReadSegment(_day, _day.AddHours(3), MetricReadSource.Hours),
                new MetricReadSegment(_day.AddHours(3), _day.AddHours(3).AddMinutes(10), MetricReadSource.FiveMinutes),
                new MetricReadSegment(_day.AddHours(3).AddMinutes(10), to, MetricReadSource.Points),
            ],
            segments);
    }

    [Fact]
    public void Split_UnrolledTimes_ReadTheirPoints()
    {
        // A day the backfill has not reached, and an hour with points that arrived after it was rolled up.
        var segments = MetricReadPlan.Split(
            _day,
            _day.AddDays(3),
            _hour,
            fiveMinutesUpTo: _day.AddDays(4),
            hoursUpTo: _day.AddDays(4),
            unrolled:
            [
                (_day.AddDays(1), _day.AddDays(2)),
                (_day.AddDays(2).AddHours(5), _day.AddDays(2).AddHours(6)),
            ]);

        Assert.Equal(
            [
                new MetricReadSegment(_day, _day.AddDays(1), MetricReadSource.Hours),
                new MetricReadSegment(_day.AddDays(1), _day.AddDays(2), MetricReadSource.Points),
                new MetricReadSegment(_day.AddDays(2), _day.AddDays(2).AddHours(5), MetricReadSource.Hours),
                new MetricReadSegment(
                    _day.AddDays(2).AddHours(5),
                    _day.AddDays(2).AddHours(6),
                    MetricReadSource.Points),
                new MetricReadSegment(_day.AddDays(2).AddHours(6), _day.AddDays(3), MetricReadSource.Hours),
            ],
            segments);
    }

    [Fact]
    public void Split_NothingRolledUpYet_ReadsThePoints()
    {
        var segments = MetricReadPlan.Split(
            _day,
            _day.AddHours(6),
            _hour,
            fiveMinutesUpTo: _day.AddHours(-3),
            hoursUpTo: _day.AddHours(-3),
            unrolled: []);

        Assert.Equal([new MetricReadSegment(_day, _day.AddHours(6), MetricReadSource.Points)], segments);
    }
}
