namespace Veyra.Query.Logs;

/// <summary>Why a log query does not parse, and where, so the bar can underline the place.</summary>
/// <param name="Position">The UTF-16 offset in the text where the problem is, from 0.</param>
/// <param name="Expected">What was expected there, in a few words, such as <c>a value</c>.</param>
/// <param name="Message">A sentence the bar can show as it is.</param>
internal sealed record LogQueryError(int Position, string Expected, string Message);
