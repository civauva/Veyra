using Veyra.Query.Logs;

namespace Veyra.Query.Incidents.Api;

/// <summary>Warnings and worse from the affected blocks in the incident's window, newest first (ADR 0026).</summary>
/// <param name="Items">At most 50 records, as <c>GET /api/v1/logs</c> returns them.</param>
/// <param name="Query">The log query (ADR 0024) that shows all of them on the Logs screen, with the window.</param>
/// <param name="Total">How many records match in the window; <see langword="null"/> when no block has logs.</param>
public sealed record IncidentLogsResponse(IReadOnlyList<LogEntryResponse> Items, string Query, long? Total);
