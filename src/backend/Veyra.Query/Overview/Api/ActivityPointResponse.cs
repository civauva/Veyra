namespace Veyra.Query.Overview.Api;

/// <summary>One step of the request activity.</summary>
/// <param name="At">The start of the step.</param>
/// <param name="Requests">The HTTP requests served in it; null where no service measured the step.</param>
/// <param name="Errors">Those answered with a 5xx status; null where the requests are.</param>
public sealed record ActivityPointResponse(DateTimeOffset At, long? Requests, long? Errors);
