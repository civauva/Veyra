namespace Veyra.Core.Traces;

/// <summary>How a span ended, as OpenTelemetry defines it; the numbers are the OTLP values.</summary>
public enum SpanStatus
{
    /// <summary>The sender did not say, which counts as success.</summary>
    Unset = 0,

    /// <summary>The sender marked the span as successful.</summary>
    Ok = 1,

    /// <summary>The operation failed.</summary>
    Error = 2,
}
