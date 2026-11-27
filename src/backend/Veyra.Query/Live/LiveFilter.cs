using Veyra.Query.Live.Api;

namespace Veyra.Query.Live;

/// <summary>
/// What the Live tab is filtered by (ADR 0020, VEY-66): a session is listed when it has every value given. The server
/// filters, so the screen lists what it receives; the tiles and findings stay the whole server's.
/// </summary>
/// <param name="Database">The database a session uses.</param>
/// <param name="Program">The program that opened it.</param>
/// <param name="Host">The machine it comes from.</param>
/// <param name="Login">Its login.</param>
/// <param name="Wait">The wait type its request waits on.</param>
public sealed record LiveFilter(
    string? Database = null,
    string? Program = null,
    string? Host = null,
    string? Login = null,
    string? Wait = null)
{
    /// <summary>Whether no value is given, so every session is listed.</summary>
    public bool IsEmpty =>
        new[] { Database, Program, Host, Login, Wait }.All(string.IsNullOrEmpty);

    /// <summary>Whether a session has every value given.</summary>
    /// <param name="session">The session.</param>
    /// <returns>True when it is listed.</returns>
    public bool Meets(LiveSessionResponse session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Matches(Database, session.Database)
            && Matches(Program, session.Program)
            && Matches(Host, session.Host)
            && Matches(Login, session.Login)
            && Matches(Wait, session.WaitType);
    }

    private static bool Matches(string? wanted, string? value) =>
        string.IsNullOrEmpty(wanted) || string.Equals(wanted, value, StringComparison.Ordinal);
}
