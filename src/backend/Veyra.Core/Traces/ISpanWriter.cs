namespace Veyra.Core.Traces;

/// <summary>
/// Persists spans. The Storage module implements it; the Ingestion module writes through it without referencing
/// Storage.
/// </summary>
public interface ISpanWriter
{
    /// <summary>Stores a batch of spans in one round trip.</summary>
    /// <param name="spans">The spans to store. The batch may be empty.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every span is stored.</returns>
    Task WriteAsync(IReadOnlyList<Span> spans, CancellationToken cancellationToken);
}
