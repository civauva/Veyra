using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Telemetry;
using Veyra.Query.Streams;
using static Veyra.Query.Tests.Streams.LiveRecords;

namespace Veyra.Query.Tests.Streams;

public sealed class LiveTelemetryHubTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly QueryOptionsMonitor _options = new(new QueryOptions { LiveListeners = 2 });
    private readonly LiveTelemetryHub _hub;

    public LiveTelemetryHubTests() => _hub = new LiveTelemetryHub(_options, _clock);

    public void Dispose() => _hub.Dispose();

    [Fact]
    public void LogsWritten_MatchingRecords_ReachTheListenerInWriteOrder()
    {
        using var listener = _hub.TryOpenLogs(LiveLogFilter.All)!;

        _hub.LogsWritten([Record("a"), Record("b")]);
        _hub.LogsWritten([Record("c")]);
        _hub.DeliverPending();

        var bodies = Drain(listener).Select(item => item.Record.Body).ToList();
        Assert.Equal(["a", "b", "c"], bodies);
    }

    [Fact]
    public void LogsWritten_Sequence_GrowsWithEveryRecordWritten()
    {
        using var listener = _hub.TryOpenLogs(LiveLogFilter.All)!;

        _hub.LogsWritten([Record("a"), Record("b")]);
        _hub.DeliverPending();

        var sequences = Drain(listener).Select(item => item.Sequence).ToList();
        Assert.Equal(sequences[0] + 1, sequences[1]);
    }

    [Fact]
    public void LogsWritten_RecordsOutsideTheFilter_AreNotQueued()
    {
        var errors = LiveLogFilter.From(new LogQuery(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            LogSeverity.Error,
            null,
            null,
            null,
            1,
            null));
        using var listener = _hub.TryOpenLogs(errors)!;

        _hub.LogsWritten([Record("fine"), Record("broken", LogSeverity.Error)]);
        _hub.DeliverPending();

        Assert.Equal(["broken"], Drain(listener).Select(item => item.Record.Body));
        Assert.Equal(0, listener.TakeSkipped());
    }

    [Fact]
    public void LogsWritten_ListenerFallsBehind_DropsTheOldestAndCountsThem()
    {
        using var listener = _hub.TryOpenLogs(LiveLogFilter.All)!;
        var batch = Enumerable.Range(1, LiveLogListener.Capacity + 5).Select(index => Record($"r{index}")).ToList();

        _hub.LogsWritten(batch);
        _hub.DeliverPending();

        Assert.Equal(5, listener.TakeSkipped());
        Assert.Equal(0, listener.TakeSkipped());
        var kept = Drain(listener);
        Assert.Equal(LiveLogListener.Capacity, kept.Count);
        Assert.Equal("r6", kept[0].Record.Body);
    }

    [Fact]
    public void LogsWritten_HubFallsBehind_CountsTheDroppedBatchesThatMatchedAsSkipped()
    {
        using var listener = _hub.TryOpenLogs(LiveLogFilter.All)!;

        // Nothing delivers while the batches arrive, so the hub's own queue overflows.
        for (var index = 0; index < LiveTelemetryHub.PendingBatches + 3; index++)
        {
            _hub.LogsWritten([Record($"b{index}"), Record($"c{index}")]);
        }

        _hub.DeliverPending();

        Assert.Equal(6, listener.TakeSkipped());
        Assert.Equal("b3", Drain(listener)[0].Record.Body);
    }

    [Fact]
    public void TryOpen_BeyondTheSetting_IsRefusedUntilOneCloses()
    {
        var first = _hub.TryOpenRates();
        using var second = _hub.TryOpenLogs(LiveLogFilter.All);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(_hub.TryOpenRates());
        Assert.Equal(2, _hub.Listeners);

        first!.Dispose();

        Assert.Equal(1, _hub.Listeners);
        using var third = _hub.TryOpenRates();
        Assert.NotNull(third);
    }

    [Fact]
    public void TryOpen_SettingRaised_TakesMoreWithoutRestart()
    {
        using var first = _hub.TryOpenRates();
        using var second = _hub.TryOpenRates();

        _options.CurrentValue = new QueryOptions { LiveListeners = 3 };

        using var third = _hub.TryOpenRates();
        Assert.NotNull(third);
    }

    [Fact]
    public void Rates_LastTenSeconds_AreEventsPerSecondPerSignal()
    {
        _hub.LogsWritten(Enumerable.Range(0, 100).Select(index => Record($"r{index}")).ToList());
        _hub.SpansWritten([new LiveCount("production", 30)]);
        _hub.PointsWritten([new LiveCount("production", 2_000), new LiveCount(null, 100)]);
        _clock.Advance(TimeSpan.FromSeconds(1));

        var rate = _hub.RatesOf(null);

        Assert.Equal(new LiveRate(10, 3, 210), rate);
    }

    [Fact]
    public void Rates_ByEnvironment_CountOnlyThatEnvironment()
    {
        _hub.LogsWritten([Record("a", environment: "staging"), Record("b")]);
        _hub.PointsWritten([new LiveCount("production", 50), new LiveCount(null, 100)]);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new LiveRate(0.1, 0, 5), _hub.RatesOf(["production"]));
        Assert.Equal(new LiveRate(0.1, 0, 0), _hub.RatesOf(["staging"]));
    }

    [Fact]
    public void Rates_OlderThanTenSeconds_AreForgotten()
    {
        _hub.SpansWritten([new LiveCount(null, 50)]);
        _clock.Advance(TimeSpan.FromSeconds(5));
        _hub.SpansWritten([new LiveCount(null, 20)]);
        _clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(2, _hub.RatesOf(null).Spans);

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(new LiveRate(0, 0, 0), _hub.RatesOf(null));
    }

    [Fact]
    public void LogsWritten_NoLogListener_KeepsNothingButTheRate()
    {
        _hub.LogsWritten([Record("a")]);

        using var listener = _hub.TryOpenLogs(LiveLogFilter.All)!;
        _hub.DeliverPending();

        Assert.Empty(Drain(listener));
    }

    private static List<LiveLogRecord> Drain(LiveLogListener listener)
    {
        var items = new List<LiveLogRecord>();

        while (listener.Records.TryRead(out var item))
        {
            items.Add(item);
        }

        return items;
    }
}
