using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// Hears nothing. Stands in when no module serves the live streams, so ingestion runs on its own; the host's Query
/// module replaces it (ADR 0022).
/// </summary>
internal sealed class NoLiveTelemetry : ILiveTelemetry
{
    /// <inheritdoc />
    public void LogsWritten(IReadOnlyList<LogRecord> records)
    {
    }

    /// <inheritdoc />
    public void SpansWritten(IReadOnlyList<LiveCount> counts)
    {
    }

    /// <inheritdoc />
    public void PointsWritten(IReadOnlyList<LiveCount> counts)
    {
    }
}
