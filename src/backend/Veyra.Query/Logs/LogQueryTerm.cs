namespace Veyra.Query.Logs;

/// <summary>
/// One term of a log query, with its place in the text, so the bar's chips can edit the text without parsing it.
/// </summary>
/// <param name="Field">What the term filters on.</param>
/// <param name="Op">
/// <see cref="Is"/> or <see cref="AtLeast"/> for a field; <see langword="null"/> for a word or phrase.
/// </param>
/// <param name="Value">The value as written, without its quotes and escapes.</param>
/// <param name="Start">Where the term starts in the text, as a UTF-16 offset from 0, as JavaScript counts.</param>
/// <param name="End">Where it ends, exclusive: after its value or closing quote.</param>
/// <param name="Key">The attribute's key, for <see cref="LogQueryField.Attribute"/> only.</param>
internal sealed record LogQueryTerm(
    LogQueryField Field,
    string? Op,
    string Value,
    int Start,
    int End,
    string? Key = null)
{
    /// <summary>The operator of <c>field:value</c>.</summary>
    public const string Is = ":";

    /// <summary>The operator of <c>level&gt;=value</c>.</summary>
    public const string AtLeast = ">=";
}
