using Veyra.Core.Telemetry;

namespace Veyra.Query.Streams;

/// <summary>
/// Which new log records a live listener wants: the filters of <c>GET /api/v1/logs</c>, matched in memory as each
/// record is written, with the same meaning as the stored search gives them.
/// </summary>
internal sealed class LiveLogFilter
{
    private readonly LogSeverity? _severity;
    private readonly HashSet<string>? _services;
    private readonly HashSet<string>? _environments;
    private readonly IReadOnlyList<string>? _words;
    private readonly string? _traceId;
    private readonly string? _spanId;
    private readonly IReadOnlyDictionary<string, string>? _attributes;
    private readonly HashSet<string>? _hosts;
    private readonly HashSet<LogSeverity>? _levels;
    private readonly IReadOnlyList<string> _phrases;

    private LiveLogFilter(
        LogSeverity? severity,
        HashSet<string>? services,
        HashSet<string>? environments,
        IReadOnlyList<string>? words,
        string? traceId,
        string? spanId,
        IReadOnlyDictionary<string, string>? attributes,
        HashSet<string>? hosts = null,
        HashSet<LogSeverity>? levels = null,
        IReadOnlyList<string>? phrases = null)
    {
        _severity = severity;
        _services = services;
        _environments = environments;
        _words = words;
        _traceId = traceId;
        _spanId = spanId;
        _attributes = attributes;
        _hosts = hosts;
        _levels = levels;
        _phrases = phrases ?? [];
    }

    /// <summary>Takes every record.</summary>
    public static LiveLogFilter All { get; } = new(null, null, null, null, null, null, null);

    /// <summary>
    /// Whether matching needs the words of a record's message, so they are split once per record for every listener.
    /// </summary>
    public bool NeedsWords => _words is not null;

    /// <summary>Takes the filters of a log search; its window, page, and order do not apply to new records.</summary>
    /// <param name="query">The search, as the logs API parses it.</param>
    /// <returns>The filter.</returns>
    public static LiveLogFilter From(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new LiveLogFilter(
            query.MinimumSeverity,
            query.Services is { Count: > 0 } services ? services.ToHashSet(StringComparer.Ordinal) : null,
            query.Environments is { Count: > 0 } environments
                ? environments.ToHashSet(StringComparer.Ordinal)
                : null,
            string.IsNullOrWhiteSpace(query.Text) ? null : [.. LiveWords.Of(query.Text)],
            string.IsNullOrWhiteSpace(query.TraceId) ? null : query.TraceId.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(query.SpanId) ? null : query.SpanId.Trim().ToLowerInvariant(),
            query.Attributes is { Count: > 0 } attributes ? attributes : null,
            query.Hosts is { Count: > 0 } hosts ? hosts.ToHashSet(StringComparer.Ordinal) : null,
            query.Levels?.ToHashSet(),
            query.Phrases);
    }

    /// <summary>Whether a record passes every filter.</summary>
    /// <param name="record">The record.</param>
    /// <param name="words">
    /// The words of its message, from <see cref="LiveWords.Of"/>; needed only when <see cref="NeedsWords"/>, and split
    /// here when not given.
    /// </param>
    /// <returns><see langword="true"/> when the listener wants it.</returns>
    public bool Matches(LogRecord record, IReadOnlySet<string>? words = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_severity is { } severity && record.Severity < severity)
        {
            return false;
        }

        if (_services is not null && !_services.Contains(record.ServiceName))
        {
            return false;
        }

        if (_environments is not null && (record.Environment is null || !_environments.Contains(record.Environment)))
        {
            return false;
        }

        if (_traceId is not null && !string.Equals(record.TraceId, _traceId, StringComparison.Ordinal))
        {
            return false;
        }

        if (_spanId is not null && !string.Equals(record.SpanId, _spanId, StringComparison.Ordinal))
        {
            return false;
        }

        if (_attributes is not null && !HasAttributes(record.Attributes))
        {
            return false;
        }

        // An empty set of levels matches nothing, as it makes the stored search do.
        if (_levels is not null && !_levels.Contains(record.Severity))
        {
            return false;
        }

        if (_hosts is not null && (record.HostName is null || !_hosts.Contains(record.HostName)))
        {
            return false;
        }

        if (_phrases.Any(phrase => !record.Body.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // A text without words matches nothing, as plainto_tsquery makes the stored search do.
        if (_words is not null)
        {
            var body = words ?? LiveWords.Of(record.Body);

            return _words.Count > 0 && _words.All(body.Contains);
        }

        return true;
    }

    private bool HasAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        foreach (var (key, value) in _attributes!)
        {
            if (!attributes.TryGetValue(key, out var actual) || !string.Equals(actual, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
