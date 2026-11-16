namespace Veyra.Query.Traces.Api;

/// <summary>Why a trace failed, from its deepest failed span.</summary>
/// <param name="Service">The service of that span.</param>
/// <param name="SpanName">Its name.</param>
/// <param name="Message">Its exception message, or else its status message, or else its name.</param>
public sealed record TraceCauseResponse(string Service, string SpanName, string Message);
