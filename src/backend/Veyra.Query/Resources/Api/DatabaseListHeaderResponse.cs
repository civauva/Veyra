namespace Veyra.Query.Resources.Api;

/// <summary>The header strip of the Databases list, over every server the filters keep.</summary>
/// <param name="Servers">How many database servers.</param>
/// <param name="Silent">How many sent nothing for longer than <c>Veyra:Query:SilentAfterHosts</c>.</param>
/// <param name="Databases">Their databases, added up.</param>
/// <param name="DataBytes">The size of those databases' files, added up; null when no server reports sizes.</param>
/// <param name="GrowthBytes">
/// How much they grew per day at the pace of the last day, added up; null when no server reports a day of sizes.
/// </param>
/// <param name="LateBackups">The databases without a full backup within 26 hours, tempdb aside.</param>
/// <param name="LateBackupFirst">The late database that has gone longest without one; null when none is late.</param>
/// <param name="BlockingChains">The blocking chains in each server's newest live read, added up.</param>
/// <param name="BlockingServer">The server with the most blocking chains; null when nothing is blocked.</param>
public sealed record DatabaseListHeaderResponse(
    int Servers,
    int Silent,
    int Databases,
    double? DataBytes,
    double? GrowthBytes,
    int LateBackups,
    LateBackupResponse? LateBackupFirst,
    int BlockingChains,
    string? BlockingServer);
