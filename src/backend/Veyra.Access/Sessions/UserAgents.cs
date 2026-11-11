namespace Veyra.Access.Sessions;

/// <summary>
/// Reads the browser and system from a <c>User-Agent</c> header for the sign-in history (ADR 0011). A few patterns
/// cover the browsers people use; anything else shows its first word. No library: the description only has to help
/// a person recognise their own devices.
/// </summary>
internal static class UserAgents
{
    /// <summary>The most of a header that is stored.</summary>
    public const int MaxLength = 512;

    // Order matters: Edge and Opera also say Chrome, Chrome also says Safari, and Android also says Linux.
    private static readonly (string Marker, string Name)[] _browsers =
    [
        ("Edg/", "Edge"),
        ("OPR/", "Opera"),
        ("Firefox/", "Firefox"),
        ("Chrome/", "Chrome"),
        ("CriOS/", "Chrome"),
        ("Safari/", "Safari"),
    ];

    private static readonly (string Marker, string Name)[] _systems =
    [
        ("iPhone", "iPhone"),
        ("iPad", "iPad"),
        ("Android", "Android"),
        ("Windows", "Windows"),
        ("CrOS", "ChromeOS"),
        ("Mac OS X", "macOS"),
        ("Ubuntu", "Ubuntu"),
        ("Linux", "Linux"),
    ];

    /// <summary>Cuts a header to the stored length.</summary>
    /// <param name="userAgent">The header, or <see langword="null"/>.</param>
    /// <returns>At most <see cref="MaxLength"/> characters, or <see langword="null"/>.</returns>
    public static string? Trim(string? userAgent) =>
        string.IsNullOrEmpty(userAgent) || userAgent.Length <= MaxLength ? userAgent : userAgent[..MaxLength];

    /// <summary>Describes a header as a person would, for example "Firefox on Ubuntu".</summary>
    /// <param name="userAgent">The header, or <see langword="null"/>.</param>
    /// <returns>The description.</returns>
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown browser";
        }

        var browser = _browsers.FirstOrDefault(one => userAgent.Contains(one.Marker, StringComparison.Ordinal)).Name;
        var system = _systems.FirstOrDefault(one => userAgent.Contains(one.Marker, StringComparison.Ordinal)).Name;

        if (browser is null)
        {
            // A tool such as curl or an SDK: its name is the first word.
            return userAgent.Split(['/', ' '], 2)[0];
        }

        return system is null ? browser : $"{browser} on {system}";
    }
}
