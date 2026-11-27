namespace Veyra.Core.Telemetry;

/// <summary>The three kinds of telemetry Veyra counts as they arrive (ADR 0023).</summary>
public enum IngestSignal
{
    /// <summary>Log records.</summary>
    Logs,

    /// <summary>Spans of traces.</summary>
    Spans,

    /// <summary>Metric points.</summary>
    Points,
}
