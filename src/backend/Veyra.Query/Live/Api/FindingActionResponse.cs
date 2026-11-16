namespace Veyra.Query.Live.Api;

/// <summary>
/// Something a finding offers to do (ADR 0020). Veyra never changes a server; it offers what a person runs.
/// </summary>
/// <param name="Kind">
/// <c>copy</c> for a statement to copy, <c>logs</c> for the Log Explorer, <c>session</c> for a session's panel.
/// </param>
/// <param name="Label">The button's words, such as "Copy the statement".</param>
/// <param name="Value">
/// The statement for <c>copy</c>; the program's name for <c>logs</c>; the session ID for <c>session</c>.
/// </param>
/// <param name="Hint">For <c>copy</c>, who should run it and what it does, shown with the button.</param>
public sealed record FindingActionResponse(string Kind, string Label, string Value, string? Hint = null);
