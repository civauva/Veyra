namespace Veyra.Core.Alerts;

/// <summary>
/// That a service, database server, or container runs on a host, as the <c>host.name</c> of its series says
/// (ADR 0026). Relations between blocks are only inferred this way; nothing declares them.
/// </summary>
/// <param name="Kind">The kind of the block that runs on the host: a service, database server, or container.</param>
/// <param name="Name">Its block name.</param>
/// <param name="Environment">
/// The environment its series name, or <see langword="null"/> when they name none, which counts for every environment.
/// </param>
/// <param name="Host">The host it runs on.</param>
public sealed record BlockLink(AlertBlockKind Kind, string Name, string? Environment, string Host);
