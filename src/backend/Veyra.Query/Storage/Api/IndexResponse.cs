namespace Veyra.Query.Storage.Api;

/// <summary>One index with its use since SQL Server started (ADR 0020, area 9).</summary>
/// <param name="Database">The database.</param>
/// <param name="Table">The table.</param>
/// <param name="Name">The index.</param>
/// <param name="Type">Its type, such as <c>NONCLUSTERED</c> or <c>HEAP</c>.</param>
/// <param name="Keys">Its key columns.</param>
/// <param name="Include">Its included columns.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="Reads">Seeks, scans, and lookups by users.</param>
/// <param name="Writes">Updates by users.</param>
/// <param name="Escalations">Lock escalations.</param>
public sealed record IndexResponse(
    string Database,
    string Table,
    string Name,
    string Type,
    string Keys,
    string Include,
    double SizeBytes,
    double Reads,
    double Writes,
    double Escalations);
