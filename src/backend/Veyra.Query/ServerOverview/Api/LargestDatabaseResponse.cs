namespace Veyra.Query.ServerOverview.Api;

/// <summary>One of a SQL Server's largest databases, with its growth (ADR 0025).</summary>
/// <param name="Database">The database.</param>
/// <param name="Bytes">Its data and log files together.</param>
/// <param name="Growth24hBytes">How much it grew over the last day.</param>
/// <param name="Growth7dBytes">How much it grew over the last 7 days.</param>
public sealed record LargestDatabaseResponse(string Database, long Bytes, long? Growth24hBytes, long? Growth7dBytes);
