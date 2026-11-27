namespace Veyra.Query.Overview.Api;

/// <summary>The database servers, as the Databases list's header counts them (ADR 0022).</summary>
/// <param name="Servers">How many database servers are known.</param>
/// <param name="Databases">Their databases, added up; the servers' own are left out.</param>
public sealed record DatabasesFigureResponse(int Servers, int Databases);
