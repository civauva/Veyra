using Veyra.Core.Alerts;

namespace Veyra.Core.Telemetry;

/// <summary>
/// The block a log record, metric series, or span belongs to, decided once at ingestion from its resource (ADR 0018).
/// </summary>
/// <param name="Kind">Service, database, container, or host.</param>
/// <param name="Name">
/// The service, container, or host name; for a database, its engine and server, such as
/// <c>microsoft.sql_server sql-prod-01</c>.
/// </param>
public sealed record TelemetryBlock(AlertBlockKind Kind, string Name)
{
    /// <summary>
    /// The service of telemetry whose resource names no block, as the OpenTelemetry specification says.
    /// </summary>
    public const string UnknownService = "unknown_service";

    /// <summary>
    /// For a database, the engine: the <c>db.system.name</c> it was sent with, such as <c>microsoft.sql_server</c>;
    /// <see langword="null"/> for other kinds.
    /// </summary>
    public string? Engine => Kind == AlertBlockKind.Database ? Name.Split(' ', 2)[0] : null;

    /// <summary>
    /// For a database, the server: its <c>server.address</c>, such as <c>sql-prod-01</c>; <see langword="null"/> for
    /// other kinds.
    /// </summary>
    public string? Server => Kind == AlertBlockKind.Database ? Name.Split(' ', 2).ElementAtOrDefault(1) ?? "" : null;

    /// <summary>A database server.</summary>
    /// <param name="engine">The engine, as <c>db.system.name</c> spells it.</param>
    /// <param name="server">The server, as <c>server.address</c> spells it.</param>
    /// <returns>The block.</returns>
    public static TelemetryBlock Database(string engine, string server) =>
        new(AlertBlockKind.Database, $"{engine} {server}");

    /// <summary>
    /// Picks the block from a resource, by the first of these it has: <c>service.name</c>; <c>db.system.name</c> with
    /// <c>server.address</c>; <c>container.name</c>; <c>host.name</c>. Without any of them, it is the unknown service.
    /// </summary>
    /// <remarks>
    /// An application keeps its service even in a container or next to a database, because its SDK sets
    /// <c>service.name</c>. SQL Server's receiver names the server only in <c>service.instance.id</c>
    /// (<c>host:port</c>), which stands in for a missing <c>server.address</c>. Empty values count as missing.
    /// </remarks>
    /// <param name="resource">The resource's attributes as text.</param>
    /// <returns>The block.</returns>
    public static TelemetryBlock Of(IReadOnlyDictionary<string, string> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (Value(resource, "service.name") is { } service)
        {
            return new TelemetryBlock(AlertBlockKind.Service, service);
        }

        if (Value(resource, "db.system.name") is { } engine
            && (Value(resource, "server.address") ?? SqlServerInstance(resource, engine)) is { } server)
        {
            return Database(engine, server);
        }

        if (Value(resource, "container.name") is { } container)
        {
            return new TelemetryBlock(AlertBlockKind.Container, container);
        }

        if (Value(resource, "host.name") is { } host)
        {
            return new TelemetryBlock(AlertBlockKind.Host, host);
        }

        return new TelemetryBlock(AlertBlockKind.Service, UnknownService);
    }

    private static string? SqlServerInstance(IReadOnlyDictionary<string, string> resource, string engine) =>
        engine == "microsoft.sql_server" ? Value(resource, "service.instance.id") : null;

    private static string? Value(IReadOnlyDictionary<string, string> resource, string key) =>
        resource.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
