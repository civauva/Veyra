using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>The whole Databases list before it is cut into pages (ADR 0022, ADR 0023).</summary>
/// <param name="Servers">Every server the filters keep, sorted.</param>
/// <param name="Header">The header strip over all of them.</param>
internal sealed record DatabaseListing(IReadOnlyList<DatabaseServerListing> Servers, DatabaseListHeaderResponse Header);
