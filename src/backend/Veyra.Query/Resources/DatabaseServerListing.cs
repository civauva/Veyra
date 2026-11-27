using Veyra.Core.Telemetry;
using Veyra.Query.Metrics.Api;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>One database server of the whole Databases list, with what the Overview reads of it (ADR 0023).</summary>
/// <param name="Block">The server's block.</param>
/// <param name="Response">Its row of the list.</param>
/// <param name="Databases">
/// Its databases as the server's screen lists them, the largest first; its own are left out unless asked for.
/// </param>
/// <param name="Late">
/// Those without a full backup within 26 hours, tempdb aside; none where the engine sends no backups.
/// </param>
/// <param name="States">
/// Each database's state as SQL Server names it, such as <c>ONLINE</c>, from the agent's storage read; empty where the
/// engine or the agent does not send it.
/// </param>
internal sealed record DatabaseServerListing(
    TelemetryBlock Block,
    DatabaseItemResponse Response,
    IReadOnlyList<DatabasePartResponse> Databases,
    IReadOnlyList<DatabasePartResponse> Late,
    IReadOnlyDictionary<string, string> States);
