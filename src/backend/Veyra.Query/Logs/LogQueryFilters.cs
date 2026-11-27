using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>
/// Turns the terms of a log query into the filters the logs endpoints already take, and ANDs them with those given as
/// their own parameters (ADR 0024). So Forecast's filter panel and Console's query bar reach the same storage filters.
/// </summary>
/// <remarks>
/// Repeated terms of one field are any of them, as repeated parameters are. A field given both ways keeps only what
/// both allow; when nothing is left, as with two different traces, the query matches no record.
/// </remarks>
internal static class LogQueryFilters
{
    /// <summary>Adds a query's terms to the filters of a request.</summary>
    /// <param name="query">The filters from the request's parameters.</param>
    /// <param name="terms">The parsed terms of its <c>q</c>.</param>
    /// <returns>The query with both; the same instance without terms.</returns>
    public static LogQuery Apply(LogQuery query, IReadOnlyList<LogQueryTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(terms);

        if (terms.Count == 0)
        {
            return query;
        }

        var nothing = false;
        var services = Narrow(query.Services, Values(terms, LogQueryField.Service), ref nothing);
        var environments = Narrow(query.Environments, Values(terms, LogQueryField.Environment), ref nothing);
        var hosts = Narrow(query.Hosts, Values(terms, LogQueryField.Host), ref nothing);
        var levels = Narrow(query.Levels, Levels(terms, LogQueryTerm.Is), ref nothing);
        var traceId = One(query.TraceId, Values(terms, LogQueryField.Trace), ref nothing);
        var spanId = One(query.SpanId, Values(terms, LogQueryField.Span), ref nothing);
        var attributes = Attributes(query.Attributes, terms, ref nothing);

        var minimum = Levels(terms, LogQueryTerm.AtLeast)
            .Append(query.MinimumSeverity ?? LogSeverity.Unspecified)
            .Max();
        var texts = Values(terms, LogQueryField.Text);
        var phrases = texts.Where(text => text.Any(char.IsWhiteSpace)).ToList();
        var words = string.Join(' ', texts);

        return query with
        {
            Services = services,
            Environments = environments,
            Hosts = hosts,
            Levels = nothing ? [] : levels,
            TraceId = traceId,
            SpanId = spanId,
            Attributes = attributes,
            MinimumSeverity = minimum == LogSeverity.Unspecified ? query.MinimumSeverity : minimum,
            Text = words.Length == 0 ? query.Text
                : string.IsNullOrWhiteSpace(query.Text) ? words
                : $"{query.Text} {words}",
            Phrases = phrases.Count == 0 ? query.Phrases : [.. query.Phrases ?? [], .. phrases],
        };
    }

    /// <summary>Whether a query's filters exclude each other, so no record can match.</summary>
    /// <param name="query">The query from <see cref="Apply"/>.</param>
    /// <returns><see langword="true"/> when it matches nothing.</returns>
    public static bool MatchesNothing(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.Levels is { Count: 0 };
    }

    private static List<string> Values(IReadOnlyList<LogQueryTerm> terms, LogQueryField field) =>
        terms.Where(term => term.Field == field).Select(term => term.Value).ToList();

    private static List<LogSeverity> Levels(IReadOnlyList<LogQueryTerm> terms, string op) =>
        terms
            .Where(term => term.Field == LogQueryField.Level && term.Op == op)
            .Select(term => LogLevelNames.TryParse(term.Value, out var level)
                ? level
                : throw new ArgumentException($"The term {term.Value} is not a level.", nameof(terms)))
            .ToList();

    // Keeps the values both allow; given without values, or wanted without, the other stands alone.
    private static IReadOnlyList<T>? Narrow<T>(IReadOnlyList<T>? given, List<T> wanted, ref bool nothing)
    {
        if (wanted.Count == 0)
        {
            return given;
        }

        if (given is not { Count: > 0 })
        {
            return wanted.Distinct().ToList();
        }

        var both = given.Where(wanted.Contains).Distinct().ToList();
        nothing |= both.Count == 0;

        return both;
    }

    // A trace or a span is one value; two different ones exclude each other. Both are compared in lower case.
    private static string? One(string? given, List<string> wanted, ref bool nothing)
    {
        var values = wanted
            .Prepend(given)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        nothing |= values.Count > 1;

        return values.Count == 0 ? given : values[0];
    }

    private static IReadOnlyDictionary<string, string>? Attributes(
        IReadOnlyDictionary<string, string>? given,
        IReadOnlyList<LogQueryTerm> terms,
        ref bool nothing)
    {
        var wanted = terms.Where(term => term.Field == LogQueryField.Attribute).ToList();

        if (wanted.Count == 0)
        {
            return given;
        }

        var attributes = given is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(given, StringComparer.Ordinal);

        foreach (var term in wanted)
        {
            if (attributes.TryGetValue(term.Key!, out var value) && !string.Equals(value, term.Value, StringComparison.Ordinal))
            {
                nothing = true;
            }

            attributes[term.Key!] = term.Value;
        }

        return attributes;
    }
}
