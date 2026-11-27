namespace Veyra.Query.Jobs.Api;

/// <summary>The block a job runs on, in the shape the alert list gives its blocks.</summary>
/// <param name="Kind"><c>service</c> for a traced job, <c>database</c> for a SQL Server Agent job.</param>
/// <param name="Name">The service, or the database server's engine and server.</param>
/// <param name="Part">Always null: a job runs on a whole block.</param>
/// <param name="Id">The block's identity on the home, for example <c>service:production/billing</c>.</param>
public sealed record JobBlockResponse(string Kind, string Name, string? Part, string Id);
