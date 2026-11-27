using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Incidents.Api;

/// <summary>A chart of an affected block over the incident's window (ADR 0026).</summary>
/// <param name="Title">What it shows, such as "Error rate".</param>
/// <param name="Block">The block it is about.</param>
/// <param name="Unit"><c>ratio</c>, <c>ms</c>, <c>count</c>, <c>bytes</c>, or <c>percent</c>.</param>
/// <param name="Lines">Its lines, one value per bucket of the window.</param>
public sealed record IncidentChartResponse(
    string Title,
    IncidentBlockResponse Block,
    string Unit,
    IReadOnlyList<ChartLineResponse> Lines);
