namespace Veyra.Query.Streams.Api;

/// <summary>
/// The data of a <c>skipped</c> event: records that matched but were dropped because the browser fell behind.
/// </summary>
/// <param name="Count">How many since the previous <c>skipped</c> event.</param>
public sealed record LiveSkippedResponse(long Count);
