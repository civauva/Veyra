namespace Veyra.Core.Telemetry;

/// <summary>
/// Hears what ingestion has just written, for the live streams (ADR 0022). The Ingestion module calls it after each
/// batch is stored; the Query module implements it and serves <c>/api/v1/live</c> and <c>/api/v1/logs/stream</c> from
/// it, neither referencing the other.
/// </summary>
/// <remarks>
/// Every method is called on an ingestion writer's own thread, so an implementation must return at once: it never
/// waits for a listener, never does I/O, and never throws. A slow browser must not slow ingestion; listeners that fall
/// behind lose their oldest records instead.
/// </remarks>
public interface ILiveTelemetry
{
    /// <summary>Hands over a batch of log records that is now stored, in the order it was written.</summary>
    /// <param name="records">
    /// The records. The implementation may keep the list, so the caller must not change it afterwards.
    /// </param>
    void LogsWritten(IReadOnlyList<LogRecord> records);

    /// <summary>Counts a batch of spans that is now stored.</summary>
    /// <param name="counts">How many spans of each environment the batch held.</param>
    void SpansWritten(IReadOnlyList<LiveCount> counts);

    /// <summary>Counts a batch of metric points that is now stored.</summary>
    /// <param name="counts">How many points of each environment the batch held.</param>
    void PointsWritten(IReadOnlyList<LiveCount> counts);
}
