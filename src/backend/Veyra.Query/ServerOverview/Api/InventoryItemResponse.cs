namespace Veyra.Query.ServerOverview.Api;

/// <summary>One database of a SQL Server's inventory (ADR 0025).</summary>
/// <param name="Database">The database.</param>
/// <param name="DataBytes">Its data files.</param>
/// <param name="LogBytes">Its log files.</param>
/// <param name="GrowthPerDayBytes">How much it grew per day over the last day.</param>
/// <param name="State">Its state as SQL Server names it, such as <c>ONLINE</c>.</param>
/// <param name="RecoveryModel">Its recovery model, such as <c>FULL</c>.</param>
public sealed record InventoryItemResponse(
    string Database,
    long? DataBytes,
    long? LogBytes,
    long? GrowthPerDayBytes,
    string? State,
    string? RecoveryModel);
