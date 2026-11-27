namespace Veyra.Query.Metrics.Api;

/// <summary>The figures over the Metrics overview's cards, counted by the server over those listed (VEY-67).</summary>
/// <param name="Hosts">Hosts listed.</param>
/// <param name="FillingDisks">Listed hosts with a disk that is full within 30 days at the pace of its last day.</param>
/// <param name="Services">Services listed.</param>
/// <param name="Databases">Database servers listed.</param>
/// <param name="Containers">Containers listed.</param>
/// <param name="Restarting">Listed containers that restarted within the window.</param>
/// <param name="AnySent">
/// Whether any host, service, database server, or container sent metrics recently, listed or not: false shows how to
/// start sending, true with no cards says nothing matches.
/// </param>
public sealed record MetricsSummaryResponse(
    int Hosts,
    int FillingDisks,
    int Services,
    int Databases,
    int Containers,
    int Restarting,
    bool AnySent);
