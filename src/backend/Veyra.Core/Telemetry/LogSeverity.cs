namespace Veyra.Core.Telemetry;

/// <summary>
/// Coarse log severity, derived from the OpenTelemetry severity number ranges
/// (TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16, ERROR 17-20, FATAL 21-24).
/// </summary>
/// <remarks>
/// The numeric values are stored in the database, so existing values must never change.
/// </remarks>
public enum LogSeverity
{
    /// <summary>The sender did not set a severity, or set one outside the OTLP range.</summary>
    Unspecified = 0,

    /// <summary>Fine-grained diagnostic detail. OTLP severity numbers 1 to 4.</summary>
    Trace = 1,

    /// <summary>Debugging information. OTLP severity numbers 5 to 8.</summary>
    Debug = 2,

    /// <summary>Normal operational messages. OTLP severity numbers 9 to 12.</summary>
    Information = 3,

    /// <summary>Something unexpected that the application handled. OTLP severity numbers 13 to 16.</summary>
    Warning = 4,

    /// <summary>An operation failed. OTLP severity numbers 17 to 20.</summary>
    Error = 5,

    /// <summary>The application or a component cannot continue. OTLP severity numbers 21 to 24.</summary>
    Fatal = 6,
}
