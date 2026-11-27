using Microsoft.Extensions.Options;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Tests.Streams;

/// <summary>Query settings a test can change between steps, as a save on the Settings screen does.</summary>
internal sealed class QueryOptionsMonitor(QueryOptions value) : IOptionsMonitor<QueryOptions>
{
    public QueryOptions CurrentValue { get; set; } = value;

    public QueryOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<QueryOptions, string?> listener) => null;
}

/// <summary>Log records for the live tests.</summary>
internal static class LiveRecords
{
    public static LogRecord Record(
        string body,
        LogSeverity severity = LogSeverity.Information,
        string service = "payments",
        string? environment = "production",
        IReadOnlyDictionary<string, string>? attributes = null,
        string? traceId = null,
        string? spanId = null) => new(
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        severity,
        severity.ToString(),
        body,
        service,
        environment,
        traceId,
        spanId,
        attributes ?? new Dictionary<string, string>(),
        new Dictionary<string, string>());
}
