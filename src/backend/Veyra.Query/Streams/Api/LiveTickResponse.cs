namespace Veyra.Query.Streams.Api;

/// <summary>The data of a <c>tick</c> event, which tells screens to fetch again.</summary>
/// <param name="At">When the server sent it.</param>
public sealed record LiveTickResponse(DateTimeOffset At);
