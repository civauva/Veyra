namespace Veyra.Query.Overview.Api;

/// <summary>The HTTP requests served over the window, from <c>http.server.request.duration</c>.</summary>
/// <param name="Count">How many; null when no service sends HTTP metrics.</param>
/// <param name="Change">
/// The change against the period before, as a share: 0.12 is 12 % more; null when the period before has none.
/// </param>
public sealed record RequestsFigureResponse(long? Count, double? Change);
