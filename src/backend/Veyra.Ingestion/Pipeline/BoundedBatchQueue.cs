using System.Threading.Channels;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// A bounded buffer of batches between an OTLP endpoint and a database writer. Logs and metrics each have one.
/// </summary>
/// <remarks>
/// <para>
/// The limit counts items, not requests, and an item keeps its place until it is <em>written</em>, not just read.
/// So when PostgreSQL slows down or stops, the queue fills and senders are told to retry, instead of Veyra running
/// out of memory.
/// </para>
/// <para>
/// Queued items live only in memory. If Veyra crashes, they are lost; senders that got a success answer will not send
/// them again. The limit therefore also bounds how much a crash can lose.
/// </para>
/// </remarks>
/// <typeparam name="T">The kind of item, a log record or a metric point.</typeparam>
/// <param name="capacity">The most items queued at once.</param>
internal class BoundedBatchQueue<T>(int capacity)
{
    private readonly Channel<IReadOnlyList<T>> _channel = Channel.CreateUnbounded<IReadOnlyList<T>>(
        new UnboundedChannelOptions { SingleReader = true });

    private int _queued;

    /// <summary>The number of items accepted but not yet written.</summary>
    public int Queued => Volatile.Read(ref _queued);

    /// <summary>Queues a batch when it fits as a whole.</summary>
    /// <param name="batch">The items of one request.</param>
    /// <returns><see langword="true"/> when queued; <see langword="false"/> when it does not fit.</returns>
    public bool TryEnqueue(IReadOnlyList<T> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return true;
        }

        // Reserve room first, so concurrent requests can never overshoot the limit together.
        if (Interlocked.Add(ref _queued, batch.Count) > capacity)
        {
            Interlocked.Add(ref _queued, -batch.Count);

            return false;
        }

        _channel.Writer.TryWrite(batch);

        return true;
    }

    /// <summary>Waits for the next batch.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The next batch.</returns>
    public ValueTask<IReadOnlyList<T>> ReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    /// <summary>Takes another batch if one is ready, without waiting.</summary>
    /// <param name="batch">The batch, when one was ready.</param>
    /// <returns><see langword="true"/> when a batch was taken.</returns>
    public bool TryRead(out IReadOnlyList<T> batch) => _channel.Reader.TryRead(out batch!);

    /// <summary>Frees the room of items that are now stored.</summary>
    /// <param name="count">How many items were written.</param>
    public void MarkWritten(int count) => Interlocked.Add(ref _queued, -count);
}
