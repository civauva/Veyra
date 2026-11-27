using System.Globalization;
using Veyra.Query.Overview.Api;
using Veyra.Query.Resources;

namespace Veyra.Query.Overview;

/// <summary>
/// Lists the databases of every server on one page, worst first (ADR 0023, point 6), from the Databases list of
/// ADR 0022, so each database's figures are those of its server's row. Pure; tested without a database.
/// </summary>
internal static class OverviewDatabases
{
    /// <summary>The most databases the Overview lists when the screen asks for no number.</summary>
    public const int MaxItems = 12;

    // How SQL Server names the state of a database that is up.
    private const string _online = "ONLINE";

    /// <summary>
    /// The worst databases first: those with a late backup or a state other than online, then the largest.
    /// </summary>
    /// <param name="servers">Every server of the Databases list, with its databases; the system ones left out.</param>
    /// <param name="most">The most databases to list, as many as fit in the screen's tile.</param>
    /// <returns>The page and how many databases there are.</returns>
    public static OverviewDatabasesResponse List(IReadOnlyList<DatabaseServerListing> servers, int most = MaxItems)
    {
        ArgumentNullException.ThrowIfNull(servers);

        var all = servers
            .SelectMany(server => server.Databases.Select(database =>
            {
                var state = server.States.GetValueOrDefault(database.Name);
                var late = server.Late.Any(one => one.Name == database.Name);
                var down = state is not null && !string.Equals(state, _online, StringComparison.OrdinalIgnoreCase);

                return (Worse: late || down, Response: new OverviewDatabaseResponse(
                    database.Name,
                    server.Response.Server,
                    server.Response.Engine,
                    StateName(state),
                    database.SizeBytes,
                    database.GrowthBytesPerDay,
                    database.BackupAgeSeconds,
                    late,
                    database.Connections));
            }))
            .ToList();

        var worstFirst = all
            .OrderBy(database => !database.Worse)
            .ThenByDescending(database => database.Response.SizeBytes ?? 0)
            .ThenBy(database => database.Response.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(database => database.Response.Name, StringComparer.OrdinalIgnoreCase)
            .Take(most)
            .Select(database => database.Response)
            .ToList();

        return new OverviewDatabasesResponse(worstFirst, all.Count);
    }

    /// <summary>A state as people read it: <c>RECOVERY_PENDING</c> is <c>Recovery pending</c>.</summary>
    /// <param name="state">The state as SQL Server names it, or null.</param>
    /// <returns>The name, or null.</returns>
    public static string? StateName(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var words = state.Trim().Replace('_', ' ').ToLower(CultureInfo.InvariantCulture);

        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
