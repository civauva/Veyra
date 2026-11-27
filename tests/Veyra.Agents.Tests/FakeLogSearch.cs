using Veyra.Core.Telemetry;

namespace Veyra.Agents.Tests;

/// <summary>Answers log searches with the records a test gives it, as the agents' own logs would arrive.</summary>
internal sealed class FakeLogSearch : ILogSearch
{
    /// <summary>The searches asked for.</summary>
    public List<LogQuery> Queries { get; } = [];

    /// <summary>The records every search answers with.</summary>
    public List<StoredLogRecord> Records { get; } = [];

    /// <summary>Adds a Collector reader error, as the Collector logs one for a component.</summary>
    /// <param name="componentId">The failing component, such as <c>sqlquery/probe/0195…</c>.</param>
    /// <param name="error">The error.</param>
    /// <param name="at">When it was logged.</param>
    public void AddReaderError(string componentId, string error, DateTimeOffset at) =>
        Records.Add(new StoredLogRecord(
            Records.Count + 1,
            new LogRecord(
                Timestamp: at,
                ObservedTimestamp: at,
                Severity: LogSeverity.Error,
                SeverityText: "error",
                Body: "Error",
                ServiceName: "otelcol-contrib",
                Environment: null,
                TraceId: null,
                SpanId: null,
                Attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["otelcol.component.kind"] = "receiver",
                    ["otelcol.component.id"] = componentId,
                    ["error"] = error,
                },
                ResourceAttributes: new Dictionary<string, string>(StringComparer.Ordinal))));

    /// <inheritdoc />
    public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);

        return Task.FromResult(new LogPage(Records.ToList(), null));
    }

    /// <inheritdoc />
    public Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
        LogQuery query,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
