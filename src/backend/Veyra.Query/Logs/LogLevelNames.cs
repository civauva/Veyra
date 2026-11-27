using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>
/// The names a log query accepts for a level: the severity names in any case, and the short names people type, such as
/// <c>warn</c> and <c>info</c>.
/// </summary>
internal static class LogLevelNames
{
    private static readonly Dictionary<string, LogSeverity> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["unspecified"] = LogSeverity.Unspecified,
        ["trace"] = LogSeverity.Trace,
        ["debug"] = LogSeverity.Debug,
        ["info"] = LogSeverity.Information,
        ["information"] = LogSeverity.Information,
        ["warn"] = LogSeverity.Warning,
        ["warning"] = LogSeverity.Warning,
        ["err"] = LogSeverity.Error,
        ["error"] = LogSeverity.Error,
        ["fatal"] = LogSeverity.Fatal,
        ["critical"] = LogSeverity.Fatal,
    };

    /// <summary>Reads a level name.</summary>
    /// <param name="name">The name, such as <c>Warning</c> or <c>warn</c>.</param>
    /// <param name="severity">The severity it names.</param>
    /// <returns><see langword="false"/> when it names none.</returns>
    public static bool TryParse(string name, out LogSeverity severity) => _names.TryGetValue(name, out severity);
}
