namespace Veyra.Core.Telemetry;

/// <summary>
/// One page of search results.
/// </summary>
/// <param name="Items">The records, newest first.</param>
/// <param name="Next">Where the next page starts, or <see langword="null"/> when this is the last page.</param>
public sealed record LogPage(IReadOnlyList<StoredLogRecord> Items, LogCursor? Next);

