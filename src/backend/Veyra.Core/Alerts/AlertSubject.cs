namespace Veyra.Core.Alerts;

/// <summary>
/// What an alert is about: a block, and optionally a part of it such as a disk's mount point (ADR 0012).
/// </summary>
/// <param name="Kind">Whether the block is a service or a host.</param>
/// <param name="Name">The service or host name.</param>
/// <param name="Part">
/// The part of the block, for example <c>/var/log</c> for a disk; <see langword="null"/> when the alert is about the
/// whole block.
/// </param>
public sealed record AlertSubject(AlertBlockKind Kind, string Name, string? Part = null)
{
    /// <summary>A service as a whole.</summary>
    /// <param name="name">The service name.</param>
    /// <returns>The subject.</returns>
    public static AlertSubject Service(string name) => new(AlertBlockKind.Service, name);

    /// <summary>A host, or a part of it.</summary>
    /// <param name="name">The host name.</param>
    /// <param name="part">The part, for example a mount point.</param>
    /// <returns>The subject.</returns>
    public static AlertSubject Host(string name, string? part = null) => new(AlertBlockKind.Host, name, part);

    /// <summary>The kind as the API and the database spell it: <c>service</c> or <c>host</c>.</summary>
    public string KindName => KindNameOf(Kind);

    /// <summary>
    /// The identity of the block on the home (ADR 0007), for example <c>host:production/web-01</c>. A part does not
    /// change it: every disk of a host lights the same block.
    /// </summary>
    /// <param name="environment">The environment, or <see langword="null"/> when the sender set none.</param>
    /// <returns>The block ID.</returns>
    public string BlockId(string? environment) => $"{KindName}:{environment}/{Name}";

    /// <summary>Spells a kind as the API and the database do.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>service</c> or <c>host</c>.</returns>
    public static string KindNameOf(AlertBlockKind kind) => kind == AlertBlockKind.Host ? "host" : "service";

    /// <summary>Reads a kind as the API and the database spell it.</summary>
    /// <param name="name"><c>service</c> or <c>host</c>, in any case.</param>
    /// <param name="kind">The kind, when the name is one.</param>
    /// <returns><see langword="true"/> when the name is a kind.</returns>
    public static bool TryParseKind(string? name, out AlertBlockKind kind)
    {
        kind = AlertBlockKind.Service;

        if (string.Equals(name, "service", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(name, "host", StringComparison.OrdinalIgnoreCase))
        {
            kind = AlertBlockKind.Host;

            return true;
        }

        return false;
    }
}
