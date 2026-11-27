namespace Veyra.Query.ServerOverview.Api;

/// <summary>The figures of a SQL Server's Overview (ADR 0025).</summary>
/// <param name="Cpu">SQL Server's share of the machine's CPU, 0 to 1.</param>
/// <param name="SqlMemory">The memory SQL Server uses, in bytes.</param>
/// <param name="Sessions">User connections.</param>
/// <param name="Blocked">Blocked sessions.</param>
/// <param name="Size">The data and log files of every database together, in bytes.</param>
/// <param name="LastFullBackupSeconds">
/// The age of the oldest last full backup among the user databases the backup rule judges; it has no line or change.
/// </param>
/// <param name="BatchRequests">Batch requests per second.</param>
/// <param name="VolumeFree">The free share of the fullest volume that holds database files, with its name.</param>
public sealed record ServerFiguresResponse(
    FigureResponse Cpu,
    FigureResponse SqlMemory,
    FigureResponse Sessions,
    FigureResponse Blocked,
    FigureResponse Size,
    FigureResponse LastFullBackupSeconds,
    FigureResponse BatchRequests,
    VolumeFigureResponse VolumeFree);
