namespace Veyra.Core.Telemetry;

/// <summary>
/// Conversions between OTLP severity numbers and <see cref="LogSeverity"/>.
/// </summary>
public static class LogSeverityExtensions
{
    /// <summary>
    /// Maps an OTLP <c>severity_number</c> to a <see cref="LogSeverity"/>.
    /// </summary>
    /// <param name="severityNumber">The OTLP severity number, defined from 0 to 24.</param>
    /// <returns>
    /// The matching severity, or <see cref="LogSeverity.Unspecified"/> for 0 and for any value outside the
    /// defined range.
    /// </returns>
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
