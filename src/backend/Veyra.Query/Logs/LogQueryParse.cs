namespace Veyra.Query.Logs;

/// <summary>The terms of a log query, or why it does not parse.</summary>
/// <param name="Terms">The terms in the order written; empty when <paramref name="Error"/> is set.</param>
/// <param name="Error">The first problem, or <see langword="null"/> when the query parses.</param>
internal sealed record LogQueryParse(IReadOnlyList<LogQueryTerm> Terms, LogQueryError? Error)
{
    /// <summary>A query without terms, which matches what the other filters match.</summary>
    public static LogQueryParse Empty { get; } = new([], null);

    /// <summary>Makes the answer for a query that does not parse.</summary>
    /// <param name="error">The problem.</param>
    /// <returns>The answer, without terms.</returns>
    public static LogQueryParse Failed(LogQueryError error) => new([], error);
}
