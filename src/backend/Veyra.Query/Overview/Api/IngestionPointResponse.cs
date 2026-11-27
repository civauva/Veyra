namespace Veyra.Query.Overview.Api;

/// <summary>One step of the telemetry ingestion, from the ingest counts (ADR 0023, point 2).</summary>
/// <param name="At">The start of the step.</param>
/// <param name="Logs">The log records stored with a time in it.</param>
/// <param name="Spans">The spans stored that started in it.</param>
/// <param name="Points">The metric points stored with a time in it.</param>
public sealed record IngestionPointResponse(DateTimeOffset At, long Logs, long Spans, long Points);
