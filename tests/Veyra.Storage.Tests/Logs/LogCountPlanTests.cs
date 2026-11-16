using Veyra.Storage.Logs;

namespace Veyra.Storage.Tests.Logs;

public class LogCountPlanTests
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Split_UnalignedWindowOfHours_ReadsRecordsAtTheEdgesMinutesAroundAndHoursInside()
    {
        var from = _day.AddHours(10).AddMinutes(3).AddSeconds(27);
        var to = _day.AddHours(13).AddMinutes(41).AddSeconds(10);

        var segments = LogCountPlan.Split(from, to, TimeSpan.FromHours(1), countable: true, uncounted: []);

        Assert.Equal(
            [
                new LogCountSegment(from, _day.AddHours(10).AddMinutes(4), LogCountSource.Records),
                new LogCountSegment(_day.AddHours(10).AddMinutes(4), _day.AddHours(11), LogCountSource.Minutes),
                new LogCountSegment(_day.AddHours(11), _day.AddHours(13), LogCountSource.Hours),
                new LogCountSegment(_day.AddHours(13), _day.AddHours(13).AddMinutes(41), LogCountSource.Minutes),
                new LogCountSegment(_day.AddHours(13).AddMinutes(41), to, LogCountSource.Records),
            ],
            segments);
    }

    [Fact]
    public void Split_AlignedWindow_HasNoRecordEdges()
    {
        var segments = LogCountPlan.Split(_day, _day.AddDays(1), TimeSpan.FromHours(1), countable: true, uncounted: []);

        Assert.Equal([new LogCountSegment(_day, _day.AddDays(1), LogCountSource.Hours)], segments);
    }

    [Fact]
    public void Split_BucketsShorterThanAnHour_ReadMinutesOnly()
    {
        // An hour row cannot be shared between 5-minute buckets.
        var segments = LogCountPlan.Split(
            _day.AddHours(10),
            _day.AddHours(13),
            TimeSpan.FromMinutes(5),
            countable: true,
            uncounted: []);

        Assert.Equal([new LogCountSegment(_day.AddHours(10), _day.AddHours(13), LogCountSource.Minutes)], segments);
    }

    [Fact]
    public void Split_NoBuckets_UsesHoursWhereTheyFit()
    {
        var segments = LogCountPlan.Split(
            _day.AddHours(10).AddMinutes(30),
            _day.AddHours(12),
            bucketSize: null,
            countable: true,
            uncounted: []);

        Assert.Equal(
            [
                new LogCountSegment(_day.AddHours(10).AddMinutes(30), _day.AddHours(11), LogCountSource.Minutes),
                new LogCountSegment(_day.AddHours(11), _day.AddHours(12), LogCountSource.Hours),
            ],
            segments);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    public void Split_BucketsThatSplitAMinute_ReadTheRecords(int seconds)
    {
        var segments = LogCountPlan.Split(
            _day,
            _day.AddMinutes(20),
            TimeSpan.FromSeconds(seconds),
            countable: true,
            uncounted: []);

        Assert.Equal([new LogCountSegment(_day, _day.AddMinutes(20), LogCountSource.Records)], segments);
    }

    [Fact]
    public void Split_NotCountable_ReadsTheRecords()
    {
        var segments = LogCountPlan.Split(
            _day,
            _day.AddDays(1),
            TimeSpan.FromHours(1),
            countable: false,
            uncounted: []);

        Assert.Equal([new LogCountSegment(_day, _day.AddDays(1), LogCountSource.Records)], segments);
    }

    [Fact]
    public void Split_WithinOneMinute_ReadsTheRecords()
    {
        var from = _day.AddSeconds(5);
        var to = _day.AddSeconds(50);

        var segments = LogCountPlan.Split(from, to, bucketSize: null, countable: true, uncounted: []);

        Assert.Equal([new LogCountSegment(from, to, LogCountSource.Records)], segments);
    }

    [Fact]
    public void Split_UncountedDay_ReadsThatDayFromTheRecords()
    {
        // The backfill has not reached the middle day yet: its counts are incomplete, so its records are read.
        var segments = LogCountPlan.Split(
            _day,
            _day.AddDays(3),
            TimeSpan.FromHours(1),
            countable: true,
            uncounted: [DateOnly.FromDateTime(_day.AddDays(1).UtcDateTime)]);

        Assert.Equal(
            [
                new LogCountSegment(_day, _day.AddDays(1), LogCountSource.Hours),
                new LogCountSegment(_day.AddDays(1), _day.AddDays(2), LogCountSource.Records),
                new LogCountSegment(_day.AddDays(2), _day.AddDays(3), LogCountSource.Hours),
            ],
            segments);
    }

    [Fact]
    public void Split_UncountedDayNextToARecordEdge_MergesThem()
    {
        var from = _day.AddDays(1).AddSeconds(-20);

        var segments = LogCountPlan.Split(
            from,
            _day.AddDays(1).AddHours(2),
            TimeSpan.FromHours(1),
            countable: true,
            uncounted: [DateOnly.FromDateTime(_day.AddDays(1).UtcDateTime)]);

        Assert.Equal([new LogCountSegment(from, _day.AddDays(1).AddHours(2), LogCountSource.Records)], segments);
    }

    [Fact]
    public void BucketOf_TimeInsideTheWindow_IsAlignedToTheClockAndClampedToTheStart()
    {
        var from = _day.AddHours(10).AddMinutes(3);

        Assert.Equal(0, LogCountPlan.BucketOf(from, from, TimeSpan.FromHours(1)));
        Assert.Equal(0, LogCountPlan.BucketOf(_day.AddHours(10).AddMinutes(59), from, TimeSpan.FromHours(1)));
        Assert.Equal(1, LogCountPlan.BucketOf(_day.AddHours(11), from, TimeSpan.FromHours(1)));
        Assert.Equal(3, LogCountPlan.BucketOf(_day.AddHours(13).AddMinutes(1), from, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void BucketStarts_UnalignedWindow_StartAtTheWindowThenOnTheClock()
    {
        var from = _day.AddHours(10).AddMinutes(3);
        var to = _day.AddHours(12).AddMinutes(30);

        var starts = LogCountPlan.BucketStarts(from, to, TimeSpan.FromHours(1));

        Assert.Equal([from, _day.AddHours(11), _day.AddHours(12)], starts);
    }
}
