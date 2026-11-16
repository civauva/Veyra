namespace Veyra.Storage.Logs;

/// <summary>A part of a counting window and the table it is counted from.</summary>
/// <param name="From">The start, inclusive.</param>
/// <param name="To">The end, exclusive.</param>
/// <param name="Source">Where its records are counted.</param>
internal sealed record LogCountSegment(DateTimeOffset From, DateTimeOffset To, LogCountSource Source);
