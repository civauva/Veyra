namespace Veyra.Query.ServerOverview.Api;

/// <summary>
/// A SQL Server's Overview tab in one read (ADR 0025), as the estate's Overview is (ADR 0023): the machine, the
/// figures with their change, instance performance, core counters, I/O, waits, storage, the inventory of databases,
/// blocking, and which monitoring areas deliver data.
/// </summary>
/// <param name="From">The start of the window.</param>
/// <param name="To">The end of the window.</param>
/// <param name="Machine">The instance and the machine it runs on.</param>
/// <param name="Figures">The figures of the header, each with its line and change.</param>
/// <param name="Performance">CPU, SQL memory, and wait time over the window.</param>
/// <param name="Counters">Batch requests, compilations, page life, and waits, averaged over the window.</param>
/// <param name="Io">Reads and writes of the database files.</param>
/// <param name="Waits">The wait types that took the most time in the window, idle waits left out.</param>
/// <param name="Storage">Size, growth, the largest databases, and the volumes.</param>
/// <param name="Inventory">Each database with its sizes, growth, state, and recovery model.</param>
/// <param name="Blocking">The current blocking chains and blocking over the window.</param>
/// <param name="Coverage">Each monitoring area of ADR 0020 and whether it delivers data.</param>
public sealed record ServerOverviewResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    MachineResponse Machine,
    ServerFiguresResponse Figures,
    PerformanceResponse Performance,
    IReadOnlyList<CounterResponse> Counters,
    IoResponse Io,
    WaitsSummaryResponse Waits,
    StorageSummaryResponse Storage,
    IReadOnlyList<InventoryItemResponse> Inventory,
    BlockingSummaryResponse Blocking,
    IReadOnlyList<CoverageResponse> Coverage);
