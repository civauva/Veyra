namespace Veyra.Core.Telemetry;

/// <summary>
/// Coarse log severity, derived from the OpenTelemetry severity number ranges
/// (TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16, ERROR 17-20, FATAL 21-24).
/// </summary>
public enum LogSeverity
{
    Unspecified = 0,
    Trace = 1,
    Debug = 2,
    Information = 3,
    Warning = 4,
    Error = 5,
    Fatal = 6,
}

public static class LogSeverityExtensions
{
    /// <summary>
    /// Maps an OTLP <c>severity_number</c> (0-24) to a <see cref="LogSeverity"/>.
    /// Values outside the defined range map to <see cref="LogSeverity.Unspecified"/>.
    /// </summary>
    public static LogSeverity FromOtlpSeverityNumber(int severityNumber) => severityNumber switch
    {
        >= 1 and <= 4 => LogSeverity.Trace,
        >= 5 and <= 8 => LogSeverity.Debug,
        >= 9 and <= 12 => LogSeverity.Information,
        >= 13 and <= 16 => LogSeverity.Warning,
        >= 17 and <= 20 => LogSeverity.Error,
        >= 21 and <= 24 => LogSeverity.Fatal,
        _ => LogSeverity.Unspecified,
    };
}
