using System.Threading.Channels;

namespace Veyra.Query.Streams;

/// <summary>
/// One browser following new log records: its filter and a bounded queue of the records that matched, waiting to be
/// sent (ADR 0022).
/// </summary>
/// <remarks>
/// The hub adds records without ever waiting. When the browser reads more slowly than records arrive, the oldest
/// waiting record goes to make room and is counted, so the stream can say how many it skipped.
/// </remarks>
internal sealed class LiveLogListener : IDisposable
{
    /// <summary>The most records that wait for one listener.</summary>
    public const int Capacity = 1_000;

    private readonly Channel<LiveLogRecord> _queue;
    private readonly Action<LiveLogListener> _close;
    private long _skipped;
    private int _closed;

    /// <summary>Creates a listener; the hub calls it.</summary>
    /// <param name="filter">Which records it wants.</param>
    /// <param name="close">Takes it off the hub and frees its place.</param>
    public LiveLogListener(LiveLogFilter filter, Action<LiveLogListener> close)
    {
        Filter = filter;
        _close = close;
        _queue = Channel.CreateBounded<LiveLogRecord>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            _ => Interlocked.Increment(ref _skipped));
    }

    /// <summary>Which records it wants.</summary>
    public LiveLogFilter Filter { get; }

    /// <summary>The records waiting to be sent, in write order.</summary>
    public ChannelReader<LiveLogRecord> Records => _queue.Reader;

    /// <summary>Queues a matching record, dropping the oldest waiting one when the queue is full.</summary>
    /// <param name="record">The record.</param>
    public void Offer(LiveLogRecord record) => _queue.Writer.TryWrite(record);

    /// <summary>Counts records it wanted that were lost before they reached its queue.</summary>
    /// <param name="count">How many.</param>
    public void Skip(long count) => Interlocked.Add(ref _skipped, count);

    /// <summary>Returns how many records were skipped since the last call, and starts counting again.</summary>
    /// <returns>The number skipped.</returns>
    public long TakeSkipped() => Interlocked.Exchange(ref _skipped, 0);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _queue.Writer.TryComplete();
            _close(this);
        }
    }
}
