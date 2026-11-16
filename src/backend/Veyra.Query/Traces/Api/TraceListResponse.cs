namespace Veyra.Query.Traces.Api;

/// <summary>A page of the trace list, newest first.</summary>
/// <param name="Items">The traces.</param>
/// <param name="NextCursor">The token for the next page, or <see langword="null"/> when this is the last.</param>
public sealed record TraceListResponse(IReadOnlyList<TraceItemResponse> Items, string? NextCursor);
