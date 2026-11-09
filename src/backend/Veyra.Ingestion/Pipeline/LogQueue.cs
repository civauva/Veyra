using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The bounded buffer between the OTLP endpoints and the database writer.
/// </summary>
/// <remarks>
/// <para>
/// The limit counts records, not requests, and a record keeps its place until it is <em>written</em>, not just read.
/// So when PostgreSQL slows down or stops, the queue fills and senders are told to retry, instead of Veyra running
/// out of memory.
/// </para>
/// <para>
/// Records in the queue live only in memory. If Veyra crashes, queued records are lost; senders that got a success
/// answer will not send them again. The limit therefore also bounds how much a crash can lose.
/// </para>
/// </remarks>
/// <param name="options">The Ingestion settings, for the limit.</param>
internal sealed class LogQueue(IOptions<IngestionOptions> options)
{
    private readonly Channel<IReadOnlyList<LogRecord>> _channel = Channel.CreateUnbounded<IReadOnlyList<LogRecord>>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly int _capacity = options.Value.MaxQueueLength;

    private int _queuedRecords;

    /// <summary>The number of records accepted but not yet written.</summary>
    public int QueuedRecords => Volatile.Read(ref _queuedRecords);

    /// <summary>
    /// Queues a batch when it fits as a whole.
    /// </summary>
    /// <param name="batch">The records of one request.</param>
    /// <returns><see langword="true"/> when queued; <see langword="false"/> when it does not fit.</returns>
    public bool TryEnqueue(IReadOnlyList<LogRecord> batch)
    {
        if (batch.Count == 0)
        {
            return true;
        }

        // Reserve room first, so concurrent requests can never overshoot the limit together.
        if (Interlocked.Add(ref _queuedRecords, batch.Count) > _capacity)
        {
            Interlocked.Add(ref _queuedRecords, -batch.Count);

            return false;
        }

        _channel.Writer.TryWrite(batch);

        return true;
    }

    /// <summary>Waits for the next batch.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The next batch.</returns>
    public ValueTask<IReadOnlyList<LogRecord>> ReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    /// <summary>Takes another batch if one is ready, without waiting.</summary>
    /// <param name="batch">The batch, when one was ready.</param>
    /// <returns><see langword="true"/> when a batch was taken.</returns>
    public bool TryRead(out IReadOnlyList<LogRecord> batch) => _channel.Reader.TryRead(out batch!);

    /// <summary>Frees the room of records that are now stored.</summary>
    /// <param name="count">How many records were written.</param>
    public void MarkWritten(int count) => Interlocked.Add(ref _queuedRecords, -count);
}
