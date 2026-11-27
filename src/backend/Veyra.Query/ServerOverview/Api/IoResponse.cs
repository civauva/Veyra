namespace Veyra.Query.ServerOverview.Api;

/// <summary>Reads and writes of a SQL Server's database files over the window (ADR 0025).</summary>
/// <param name="ReadBytesPerSecond">Bytes read per second, averaged over the window.</param>
/// <param name="WriteBytesPerSecond">Bytes written per second, averaged over the window.</param>
/// <param name="ReadLine">Bytes read per second per bucket.</param>
/// <param name="WriteLine">Bytes written per second per bucket.</param>
public sealed record IoResponse(
    double? ReadBytesPerSecond,
    double? WriteBytesPerSecond,
    IReadOnlyList<double?> ReadLine,
    IReadOnlyList<double?> WriteLine);
