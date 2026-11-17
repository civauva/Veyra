namespace Veyra.Query.Server.Api;

/// <summary>A permission the monitoring login lacks.</summary>
/// <param name="Permission">
/// The permission, such as <c>VIEW SERVER STATE</c> or <c>SELECT ON msdb.dbo.sysjobs</c>.
/// </param>
/// <param name="Tabs">The tabs of the SQL Server screen it leaves empty, by their names in the address.</param>
/// <param name="Reads">What Veyra cannot read without it, in words.</param>
public sealed record PermissionGapResponse(string Permission, IReadOnlyList<string> Tabs, string Reads);
