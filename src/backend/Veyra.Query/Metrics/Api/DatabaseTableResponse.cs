namespace Veyra.Query.Metrics.Api;

/// <summary>
/// A PostgreSQL table with dead rows: rows that updates and deletes left behind until a vacuum clears them.
/// </summary>
/// <param name="Database">The database it is in.</param>
/// <param name="Schema">Its schema.</param>
/// <param name="Name">The table name.</param>
/// <param name="LiveRows">Its live rows now.</param>
/// <param name="DeadRows">Its dead rows now.</param>
/// <param name="DeadRowsRatio">Dead rows of all its rows, from 0 to 1.</param>
/// <param name="Vacuums">How many times it was vacuumed over the last day.</param>
public sealed record DatabaseTableResponse(
    string Database,
    string? Schema,
    string Name,
    double? LiveRows,
    double? DeadRows,
    double? DeadRowsRatio,
    double? Vacuums)
{
    /// <summary>Whether it is a catalog table, or in a database the engine keeps for itself (VEY-64).</summary>
    public bool System { get; init; }
}
