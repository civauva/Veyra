using Veyra.Query.Queries.Api;

namespace Veyra.Query.Storage.Api;

/// <summary>One finding of the Storage tab's Needs doing list, with the database it is about (VEY-62, VEY-68).</summary>
/// <param name="Finding">What Veyra found, with the statement to run.</param>
/// <param name="Database">The database it is about, or <see langword="null"/> for the server's own finding.</param>
public sealed record StorageFindingResponse(PlanWarningResponse Finding, string? Database);
