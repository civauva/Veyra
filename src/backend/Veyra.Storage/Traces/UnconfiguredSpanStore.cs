using Veyra.Core.Traces;

namespace Veyra.Storage.Traces;

/// <summary>
/// Stands in for <see cref="SpanStore"/> when no database is configured, so the modules can start; every use fails
/// with a message that says what to set.
/// </summary>
internal sealed class UnconfiguredSpanStore : ISpanWriter, ITraceSearch
{
    /// <inheritdoc />
    public Task WriteAsync(IReadOnlyList<Span> spans, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<TracePage> ListAsync(TraceQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<TraceDurations> DurationsAsync(TraceQuery query, int maxPoints, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<TraceServiceCount>> ServicesAsync(
        TraceQuery query,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<TraceFigures> FiguresAsync(TraceQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<Span>> GetTraceAsync(
        string traceId,
        DateTimeOffset? around,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
