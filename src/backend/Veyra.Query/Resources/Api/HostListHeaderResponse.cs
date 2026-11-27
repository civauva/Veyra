namespace Veyra.Query.Resources.Api;

/// <summary>The header strip of the Hosts and containers list, over every item the filters keep.</summary>
/// <param name="Known">How many hosts are known: every host whose metrics are kept, silent ones too.</param>
/// <param name="Online">How many of those hosts are not silent.</param>
/// <param name="Containers">How many containers are known.</param>
/// <param name="CpuAverage">The average CPU share in use over the window, of every item that reports one.</param>
/// <param name="CpuBusiest">The item with the highest CPU share.</param>
/// <param name="MemoryAverage">The average memory share in use, of every item that reports one.</param>
/// <param name="MemoryFullest">The item with the highest memory share.</param>
/// <param name="DisksFilling">How many hosts have a disk full within 30 days at the pace of the last day.</param>
/// <param name="DisksFillingFirst">The disk that is full soonest; null when none is filling.</param>
public sealed record HostListHeaderResponse(
    int Known,
    int Online,
    int Containers,
    double? CpuAverage,
    NamedRatioResponse? CpuBusiest,
    double? MemoryAverage,
    NamedRatioResponse? MemoryFullest,
    int DisksFilling,
    DiskFillingResponse? DisksFillingFirst);
