namespace Veyra.Access.Users;

/// <summary>
/// The looks of the web app a person or an Admin can pick (ADR 0021). The one list every check, setting, and message
/// reads, so a new look such as City is added here alone.
/// </summary>
internal static class Looks
{
    /// <summary>Today's weather look.</summary>
    public const string Forecast = "forecast";

    /// <summary>The dense operations console.</summary>
    public const string Console = "console";

    /// <summary>Every look, in the order the Settings screen offers them.</summary>
    public static IReadOnlyList<string> All { get; } = [Forecast, Console];

    /// <summary>The problem a request with a look that is not one of <see cref="All"/> gets.</summary>
    public static string Unknown { get; } = $"The look must be one of: {string.Join(", ", All)}.";

    /// <summary>Whether a value is one of the looks, exactly as the API writes it.</summary>
    /// <param name="look">The value, such as <c>console</c>.</param>
    /// <returns><see langword="true"/> for a look of the app.</returns>
    public static bool IsKnown(string? look) => look is not null && All.Contains(look, StringComparer.Ordinal);
}
