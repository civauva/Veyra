namespace Veyra.Query.Logs;

/// <summary>One page of log records.</summary>
/// <param name="Items">The records, newest first.</param>
/// <param name="NextCursor">
/// Pass it as <c>cursor</c> to get the next page; <see langword="null"/> on the last page.
/// </param>
public sealed record LogsResponse(IReadOnlyList<LogEntryResponse> Items, string? NextCursor);
