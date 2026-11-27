namespace Veyra.Query.Logs;

/// <summary>Why the filters of a logs request cannot be used; the endpoints answer it as <c>400</c>.</summary>
/// <param name="Title">The problem's title, a sentence that can be shown as it is.</param>
/// <param name="Position">For a query that does not parse, where in <c>q</c> the problem is.</param>
/// <param name="Expected">For a query that does not parse, what was expected there.</param>
internal sealed record LogFilterProblem(string Title, int? Position = null, string? Expected = null);
