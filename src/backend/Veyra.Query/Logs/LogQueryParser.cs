using System.Globalization;
using System.Text;

namespace Veyra.Query.Logs;

/// <summary>
/// Parses the query of the log bar (ADR 0024): terms joined by <c>AND</c> or a space, each a word, a "quoted phrase",
/// <c>field:value</c>, or <c>level&gt;=value</c>. The server alone parses it, so every look and every script reads a
/// query the same way.
/// </summary>
/// <remarks>
/// There is no <c>OR</c> or <c>NOT</c> yet. They are refused rather than read as words, so a query written for them
/// can never quietly mean something else, and adding them later breaks no query that parses today.
/// </remarks>
internal static class LogQueryParser
{
    /// <summary>The longest query, in characters; a saved view holds at most as much.</summary>
    public const int MaxLength = 2_000;

    private const string _and = "AND";

    /// <summary>Splits a query into its terms, or finds its first problem.</summary>
    /// <param name="text">The query; <see langword="null"/> or blank has no terms.</param>
    /// <returns>The terms, or the error.</returns>
    public static LogQueryParse Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LogQueryParse.Empty;
        }

        if (text.Length > MaxLength)
        {
            return LogQueryParse.Failed(new LogQueryError(
                MaxLength,
                $"at most {MaxLength} characters",
                $"A query has at most {MaxLength.ToString("N0", CultureInfo.InvariantCulture)} characters."));
        }

        return new Scanner(text).Read();
    }

    /// <summary>Reads one query from start to end. It is used once.</summary>
    /// <param name="text">The query.</param>
    private sealed class Scanner(string text)
    {
        private readonly List<LogQueryTerm> _terms = [];

        private int _position;

        // Where the AND that waits for its next term starts, if one does.
        private int? _pendingAnd;

        public LogQueryParse Read()
        {
            while (true)
            {
                SkipSpaces();

                if (_position == text.Length)
                {
                    break;
                }

                if (ReadTerm() is { } error)
                {
                    return LogQueryParse.Failed(error);
                }
            }

            if (_pendingAnd is not null)
            {
                return LogQueryParse.Failed(new LogQueryError(text.Length, "a term", "Expected a term after AND."));
            }

            return new LogQueryParse(_terms, null);
        }

        private LogQueryError? ReadTerm()
        {
            var start = _position;

            if (text[start] == '"')
            {
                if (ReadQuoted(out var phrase) is { } quoteError)
                {
                    return quoteError;
                }

                return AfterValue() ?? Add(new LogQueryTerm(LogQueryField.Text, null, phrase, start, _position));
            }

            var nameEnd = start;

            while (nameEnd < text.Length && !EndsName(nameEnd))
            {
                nameEnd++;
            }

            var name = text[start..nameEnd];
            _position = nameEnd;

            if (nameEnd == text.Length || char.IsWhiteSpace(text[nameEnd]))
            {
                return ReadWord(name, start);
            }

            if (text[nameEnd] == '"')
            {
                return new LogQueryError(nameEnd, "a space", "Expected a space before the quote.");
            }

            var op = text[nameEnd] == ':' ? LogQueryTerm.Is : LogQueryTerm.AtLeast;

            if (name.Length == 0)
            {
                return new LogQueryError(start, "a field name", $"Expected a field name before {op}.");
            }

            _position = nameEnd + op.Length;

            return ReadField(name, op, start);
        }

        private LogQueryError? ReadWord(string word, int start)
        {
            if (word == _and)
            {
                if (_pendingAnd is not null)
                {
                    return new LogQueryError(start, "a term", "Expected a term between the two ANDs.");
                }

                if (_terms.Count == 0)
                {
                    return new LogQueryError(start, "a term", "Expected a term before AND.");
                }

                _pendingAnd = start;

                return null;
            }

            if (word is "OR" or "NOT")
            {
                return new LogQueryError(
                    start,
                    _and,
                    $"{word} is not supported yet. Join terms with AND or a space.");
            }

            return Add(new LogQueryTerm(LogQueryField.Text, null, word, start, _position));
        }

        private LogQueryError? ReadField(string name, string op, int start)
        {
            var key = name.ToLowerInvariant();

            if (key == "time")
            {
                return new LogQueryError(
                    start,
                    "a term without time",
                    "The time comes from the range at the top of the screen, not from the query.");
            }

            if (op == LogQueryTerm.AtLeast && key != "level")
            {
                return new LogQueryError(
                    start,
                    "level",
                    $"Only level compares with >=. Write {name}:value for a {name}.");
            }

            var valueStart = _position;

            if (valueStart == text.Length || char.IsWhiteSpace(text[valueStart]))
            {
                return new LogQueryError(valueStart, "a value", $"Expected a value after {name}{op}.");
            }

            if (ReadValue(out var value) is { } valueError)
            {
                return valueError;
            }

            if (key == "level" && !LogLevelNames.TryParse(value, out _))
            {
                return new LogQueryError(
                    valueStart,
                    "a level",
                    "Expected a level: trace, debug, information, warning, error, or fatal.");
            }

            var field = key switch
            {
                "service" => LogQueryField.Service,
                "env" or "environment" => LogQueryField.Environment,
                "host" => LogQueryField.Host,
                "level" => LogQueryField.Level,
                "trace" => LogQueryField.Trace,
                "span" => LogQueryField.Span,
                _ => LogQueryField.Attribute,
            };

            return Add(new LogQueryTerm(
                field,
                op,
                value,
                start,
                _position,
                field == LogQueryField.Attribute ? name : null));
        }

        // A value is quoted, or runs to the next space. A bare value may hold colons, as URLs and times do.
        private LogQueryError? ReadValue(out string value)
        {
            if (text[_position] == '"')
            {
                return ReadQuoted(out value) ?? AfterValue();
            }

            var start = _position;

            while (_position < text.Length && !char.IsWhiteSpace(text[_position]))
            {
                if (text[_position] == '"')
                {
                    value = string.Empty;

                    return new LogQueryError(_position, "a space", "Expected a space before the quote.");
                }

                _position++;
            }

            value = text[start.._position];

            return null;
        }

        // Inside quotes, \" is a quote and \\ a backslash; any other backslash is kept as written.
        private LogQueryError? ReadQuoted(out string value)
        {
            var builder = new StringBuilder();
            value = string.Empty;
            _position++;

            while (_position < text.Length)
            {
                var current = text[_position];

                if (current == '\\' && _position + 1 < text.Length && text[_position + 1] is '"' or '\\')
                {
                    builder.Append(text[_position + 1]);
                    _position += 2;

                    continue;
                }

                if (current == '"')
                {
                    if (string.IsNullOrWhiteSpace(builder.ToString()))
                    {
                        return new LogQueryError(
                            _position,
                            "text between the quotes",
                            "Expected text between the quotes.");
                    }

                    _position++;
                    value = builder.ToString();

                    return null;
                }

                builder.Append(current);
                _position++;
            }

            return new LogQueryError(text.Length, "a closing quote", "Expected a closing quote.");
        }

        private LogQueryError? AfterValue() =>
            _position < text.Length && !char.IsWhiteSpace(text[_position])
                ? new LogQueryError(_position, "a space", "Expected a space after the closing quote.")
                : null;

        private LogQueryError? Add(LogQueryTerm term)
        {
            _terms.Add(term);
            _pendingAnd = null;

            return null;
        }

        // A name ends at a space, a colon, a quote, or >=.
        private bool EndsName(int index) =>
            char.IsWhiteSpace(text[index])
            || text[index] is ':' or '"'
            || (text[index] == '>' && index + 1 < text.Length && text[index + 1] == '=');

        private void SkipSpaces()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }
    }
}
