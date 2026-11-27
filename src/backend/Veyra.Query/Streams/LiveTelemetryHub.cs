using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Streams;

/// <summary>
/// Takes what ingestion writes and hands it to the live streams (ADR 0022): rates for the top bar, and new log records
/// for each listener that wants them.
/// </summary>
/// <remarks>
/// <para>
/// Ingestion's writers call it on their own threads, so each call only counts and queues: a batch of records waits in
/// a short queue of its own, and this service's loop matches it against every listener and fills their queues. When
/// that loop falls behind, the oldest batch goes, and every listener it would have reached is told how many of its
/// records were skipped. A slow browser never slows a writer.
/// </para>
/// <para>
/// The number of listeners on the instance is capped by <c>Veyra:Query:LiveListeners</c>, read as each one opens, so a
/// change on the Settings screen applies to the next browser.
/// </para>
/// </remarks>
internal sealed class LiveTelemetryHub : BackgroundService, ILiveTelemetry
{
    /// <summary>The most written batches that wait to be matched against listeners.</summary>
    public const int PendingBatches = 64;

    private readonly IOptionsMonitor<QueryOptions> _options;
    private readonly LiveRates _rates;
    private readonly Channel<IReadOnlyList<LogRecord>> _pending;
    private readonly Lock _lock = new();

    // Replaced, never changed, so the loop can read it without the lock.
    private LiveLogListener[] _logListeners = [];
    private int _listeners;
    private long _sequence;

    /// <summary>Creates the hub.</summary>
    /// <param name="options">The Query settings, for the most listeners.</param>
    /// <param name="clock">The clock for the rates.</param>
    public LiveTelemetryHub(IOptionsMonitor<QueryOptions> options, TimeProvider clock)
    {
        _options = options;
        _rates = new LiveRates(clock);
        _pending = Channel.CreateBounded<IReadOnlyList<LogRecord>>(
            new BoundedChannelOptions(PendingBatches)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            Dropped);
    }

    /// <summary>How many live streams are open on this instance.</summary>
    public int Listeners => Volatile.Read(ref _listeners);

    /// <inheritdoc />
    public void LogsWritten(IReadOnlyList<LogRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var count in LiveCount.Of(records, record => record.Environment))
        {
            _rates.Add(LiveSignal.Logs, count.Environment, count.Count);
        }

        // Without anyone following logs, records are counted but not kept.
        if (Volatile.Read(ref _logListeners).Length > 0)
        {
            _pending.Writer.TryWrite(records);
        }
    }

    /// <inheritdoc />
    public void SpansWritten(IReadOnlyList<LiveCount> counts) => Count(LiveSignal.Spans, counts);

    /// <inheritdoc />
    public void PointsWritten(IReadOnlyList<LiveCount> counts) => Count(LiveSignal.Points, counts);

    /// <summary>Reads the rates of the last ten seconds.</summary>
    /// <param name="environments">Only these environments, or <see langword="null"/> for all.</param>
    /// <returns>Events per second.</returns>
    public LiveRate RatesOf(IReadOnlyCollection<string>? environments) => _rates.Read(environments);

    /// <summary>Opens a stream of rates, when there is room for one more listener.</summary>
    /// <returns>Its place, to dispose when the stream ends; <see langword="null"/> when the instance is full.</returns>
    public LiveLease? TryOpenRates()
    {
        lock (_lock)
        {
            if (!HasRoom())
            {
                return null;
            }

            _listeners++;
        }

        return new LiveLease(Release);
    }

    /// <summary>Opens a stream of new log records, when there is room for one more listener.</summary>
    /// <param name="filter">Which records it wants.</param>
    /// <returns>
    /// The listener, to dispose when the stream ends; <see langword="null"/> when the instance is full.
    /// </returns>
    public LiveLogListener? TryOpenLogs(LiveLogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var listener = new LiveLogListener(filter, Close);

        lock (_lock)
        {
            if (!HasRoom())
            {
                return null;
            }

            _listeners++;
            _logListeners = [.. _logListeners, listener];
        }

        return listener;
    }

    /// <summary>Matches every waiting batch against the listeners and fills their queues.</summary>
    public void DeliverPending()
    {
        while (_pending.Reader.TryRead(out var batch))
        {
            Deliver(batch);
        }
    }

    /// <summary>Delivers batches as they arrive, until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _pending.Reader.WaitToReadAsync(stoppingToken))
            {
                DeliverPending();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: the open streams end with the requests that hold them.
        }
    }

    private void Count(LiveSignal signal, IReadOnlyList<LiveCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        foreach (var count in counts)
        {
            _rates.Add(signal, count.Environment, count.Count);
        }
    }

    private bool HasRoom() => _listeners < _options.CurrentValue.LiveListeners;

    private void Deliver(IReadOnlyList<LogRecord> batch)
    {
        var listeners = Volatile.Read(ref _logListeners);
        var needsWords = listeners.Any(listener => listener.Filter.NeedsWords);

        foreach (var record in batch)
        {
            var item = new LiveLogRecord(Interlocked.Increment(ref _sequence), record);

            // Split once per record, however many listeners search for words.
            var words = needsWords ? LiveWords.Of(record.Body) : null;

            foreach (var listener in listeners)
            {
                if (listener.Filter.Matches(record, words))
                {
                    listener.Offer(item);
                }
            }
        }
    }

    // Called by the queue when a batch had to go to make room: the records keep their numbers, so the sequence still
    // shows the gap, and each listener learns how many it would have had.
    private void Dropped(IReadOnlyList<LogRecord> batch)
    {
        Interlocked.Add(ref _sequence, batch.Count);

        var listeners = Volatile.Read(ref _logListeners);
        var needsWords = listeners.Any(listener => listener.Filter.NeedsWords);
        var missed = new long[listeners.Length];

        foreach (var record in batch)
        {
            var words = needsWords ? LiveWords.Of(record.Body) : null;

            for (var index = 0; index < listeners.Length; index++)
            {
                if (listeners[index].Filter.Matches(record, words))
                {
                    missed[index]++;
                }
            }
        }

        for (var index = 0; index < listeners.Length; index++)
        {
            if (missed[index] > 0)
            {
                listeners[index].Skip(missed[index]);
            }
        }
    }

    private void Release()
    {
        lock (_lock)
        {
            _listeners--;
        }
    }

    private void Close(LiveLogListener listener)
    {
        lock (_lock)
        {
            _listeners--;
            _logListeners = _logListeners.Where(other => !ReferenceEquals(other, listener)).ToArray();
        }
    }
}
