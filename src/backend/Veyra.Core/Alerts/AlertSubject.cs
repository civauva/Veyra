namespace Veyra.Core.Alerts;

/// <summary>
/// What an alert is about: a block, and optionally a part of it such as a disk's mount point (ADR 0012).
/// </summary>
/// <param name="Kind">Whether the block is a service, a host, a database server, or a container.</param>
/// <param name="Name">The block name; for a database server, its engine and server (ADR 0018).</param>
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

    /// <summary>A database server, or a part of it such as one of its databases or SQL Agent jobs.</summary>
    /// <param name="name">The engine and server, such as <c>microsoft.sql_server sql-prod-01</c>.</param>
    /// <param name="part">The part, for example a database name.</param>
    /// <returns>The subject.</returns>
    public static AlertSubject Database(string name, string? part = null) => new(AlertBlockKind.Database, name, part);

    /// <summary>
    /// A Docker container. Its host stands in the part: containers on two hosts may share a name, and alerts and links
    /// need the host to say which one they mean (ADR 0018).
    /// </summary>
    /// <param name="name">The container name, from <c>container.name</c>.</param>
    /// <param name="host">The host it runs on, from <c>host.name</c>, if known.</param>
    /// <returns>The subject.</returns>
    public static AlertSubject Container(string name, string? host) => new(AlertBlockKind.Container, name, host);

    /// <summary>
    /// The kind as the API and the database spell it: <c>service</c>, <c>host</c>, <c>database</c>, or
    /// <c>container</c>.
    /// </summary>
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
    /// <returns><c>service</c>, <c>host</c>, <c>database</c>, or <c>container</c>.</returns>
    public static string KindNameOf(AlertBlockKind kind) => kind switch
    {
        AlertBlockKind.Host => "host",
        AlertBlockKind.Database => "database",
        AlertBlockKind.Container => "container",
        _ => "service",
    };

    /// <summary>Reads a kind as the API and the database spell it.</summary>
    /// <param name="name"><c>service</c>, <c>host</c>, <c>database</c>, or <c>container</c>, in any case.</param>
    /// <param name="kind">The kind, when the name is one.</param>
    /// <returns><see langword="true"/> when the name is a kind.</returns>
    public static bool TryParseKind(string? name, out AlertBlockKind kind)
    {
        foreach (var candidate in Enum.GetValues<AlertBlockKind>())
        {
            if (string.Equals(name, KindNameOf(candidate), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;

                return true;
            }
        }

        kind = AlertBlockKind.Service;

        return false;
    }
}
