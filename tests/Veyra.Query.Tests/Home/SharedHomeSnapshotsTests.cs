using Microsoft.Extensions.Time.Testing;
using Veyra.Query.Home;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.Home;

public class SharedHomeSnapshotsTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 7, 7, 34, 12, TimeSpan.Zero);

    private readonly FakeLogSearch _search = new();
    private readonly FakeTimeProvider _clock = new(_now);

    [Fact]
    public async Task GetAsync_SameEnvironmentWithinFiveSeconds_BuildsOnce()
    {
        var homes = Homes();

        var first = await homes.GetAsync("production", CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(4));
        var second = await homes.GetAsync("production", CancellationToken.None);

        // Every open home asks every 15 seconds: twenty browsers must cost what one does (ADR 0015).
        Assert.Same(first, second);
        Assert.Equal(1, Builds());
    }

    [Fact]
    public async Task GetAsync_AfterFiveSeconds_BuildsAgain()
    {
        var homes = Homes();

        await homes.GetAsync("production", CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await homes.GetAsync("production", CancellationToken.None);

        Assert.Equal(2, Builds());
    }

    [Fact]
    public async Task GetAsync_OtherEnvironment_BuildsItsOwn()
    {
        var homes = Homes();

        var production = await homes.GetAsync("production", CancellationToken.None);
        var all = await homes.GetAsync(null, CancellationToken.None);

        Assert.Equal(("production", null), (production.Environment, all.Environment));
        Assert.Equal(2, Builds());
    }

    [Fact]
    public async Task GetAsync_AtTheSameTime_ShareOneBuild()
    {
        var homes = Homes();

        var both = await Task.WhenAll(
            homes.GetAsync("production", CancellationToken.None),
            homes.GetAsync("production", CancellationToken.None));

        Assert.Same(both[0], both[1]);
        Assert.Equal(1, Builds());
    }

    [Fact]
    public async Task GetAsync_BuildFailed_TriesAgainOnTheNextRequest()
    {
        var failing = true;
        var homes = new SharedHomeSnapshots(
            (environment, cancellationToken) => failing
                ? Task.FromException<HomeResponse>(new InvalidOperationException("database down"))
                : Builder().BuildAsync(environment, cancellationToken),
            _clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => homes.GetAsync(null, CancellationToken.None));
        failing = false;
        var home = await homes.GetAsync(null, CancellationToken.None);

        Assert.Null(home.Environment);
    }

    // Every build counts the last day by hour; the seven days behind the expected load are read once an hour.
    private int Builds() =>
        _search.Summaries.Count(summary => summary.Query.To - summary.Query.From < TimeSpan.FromDays(2));

    private SharedHomeSnapshots Homes() => new(Builder().BuildAsync, _clock);

    private HomeSnapshotBuilder Builder() => new(
        new FakeLogRates(),
        _search,
        new FakeAlertStates(),
        new FakeLogStorageUsage(),
        new FakeMetricSearch(),
        _clock);
}
