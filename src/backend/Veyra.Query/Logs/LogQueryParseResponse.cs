namespace Veyra.Query.Logs;

/// <summary>The terms of a log query, as <c>GET /api/v1/logs/parse</c> answers them for the bar's chips.</summary>
/// <param name="Terms">The terms in the order written.</param>
public sealed record LogQueryParseResponse(IReadOnlyList<LogQueryTermResponse> Terms);
