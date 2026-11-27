namespace Veyra.Query.Streams;

/// <summary>What was written per second over the last ten whole seconds.</summary>
/// <param name="Logs">Log records per second.</param>
/// <param name="Spans">Spans per second.</param>
/// <param name="Points">Metric points per second.</param>
internal sealed record LiveRate(double Logs, double Spans, double Points);
