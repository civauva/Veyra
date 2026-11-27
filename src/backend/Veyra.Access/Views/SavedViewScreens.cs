namespace Veyra.Access.Views;

/// <summary>
/// The screens that keep saved views (ADR 0024). The one list every check and message reads, so a screen that gets
/// views is added here alone.
/// </summary>
internal static class SavedViewScreens
{
    /// <summary>The Log explorer.</summary>
    public const string Logs = "logs";

    /// <summary>Every screen with saved views.</summary>
    public static IReadOnlyList<string> All { get; } = [Logs];

    /// <summary>The problem a request for a screen that is not one of <see cref="All"/> gets.</summary>
    public static string Unknown { get; } = $"The screen must be one of: {string.Join(", ", All)}.";

    /// <summary>Whether a value is one of the screens, exactly as the API writes it.</summary>
    /// <param name="screen">The value, such as <c>logs</c>.</param>
    /// <returns><see langword="true"/> for a screen with saved views.</returns>
    public static bool IsKnown(string? screen) => screen is not null && All.Contains(screen, StringComparer.Ordinal);
}
