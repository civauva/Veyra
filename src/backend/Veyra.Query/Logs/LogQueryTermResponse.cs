namespace Veyra.Query.Logs;

/// <summary>One term of a log query and its place in the text (ADR 0024).</summary>
/// <param name="Field">
/// <c>text</c> for a word or phrase; otherwise <c>service</c>, <c>environment</c>, <c>host</c>, <c>level</c>,
/// <c>trace</c>, <c>span</c>, or <c>attribute</c>.
/// </param>
/// <param name="Op"><c>:</c> or <c>&gt;=</c>; <see langword="null"/> for a word or phrase.</param>
/// <param name="Value">The value without its quotes and escapes.</param>
/// <param name="Start">Where the term starts in the text, as a UTF-16 offset from 0, as JavaScript counts.</param>
/// <param name="End">Where it ends, exclusive.</param>
/// <param name="Key">The attribute's key, for <c>attribute</c> terms only.</param>
public sealed record LogQueryTermResponse(string Field, string? Op, string Value, int Start, int End, string? Key);
