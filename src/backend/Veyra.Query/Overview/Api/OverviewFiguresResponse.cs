namespace Veyra.Query.Overview.Api;

/// <summary>The figures across the top of the Overview (ADR 0023).</summary>
/// <param name="Services">How the services stand, from their open alerts.</param>
/// <param name="Hosts">How many hosts are known and how many of them are online.</param>
/// <param name="Databases">How many database servers and databases are known.</param>
/// <param name="Requests">The HTTP requests served over the window.</param>
/// <param name="ErrorRatio">The share of those requests answered with a 5xx status.</param>
/// <param name="OpenAlerts">The alerts open now, by severity.</param>
/// <param name="Jobs">How the SQL Server Agent job runs of the window went.</param>
/// <param name="Availability">The share of the last 30 days in which no service had a warning open.</param>
public sealed record OverviewFiguresResponse(
    ServicesFigureResponse Services,
    HostsFigureResponse Hosts,
    DatabasesFigureResponse Databases,
    RequestsFigureResponse Requests,
    ErrorRatioFigureResponse ErrorRatio,
    OpenAlertsFigureResponse OpenAlerts,
    JobsFigureResponse Jobs,
    AvailabilityFigureResponse Availability);
