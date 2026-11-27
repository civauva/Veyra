namespace Veyra.Query.Streams.Api;

/// <summary>The data of a <c>rate</c> event: what was written per second over the last ten seconds.</summary>
/// <param name="Logs">Log records per second.</param>
/// <param name="Spans">Spans per second.</param>
/// <param name="Points">Metric points per second.</param>
public sealed record LiveRateResponse(double Logs, double Spans, double Points);
