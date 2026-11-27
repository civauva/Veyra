using Veyra.Query.Systems.Api;

namespace Veyra.Query.Systems;

/// <summary>
/// Leaves a database server's own rows out of a list unless they were asked for (VEY-66). Every list does it on the
/// server, before it is cut to its limit, so the rows left are the applications' and the screen never filters.
/// </summary>
internal static class SystemRows
{
    private const int _maxNames = 20;

    /// <summary>Keeps the rows to list and counts the ones left out.</summary>
    /// <typeparam name="T">The row.</typeparam>
    /// <param name="rows">The rows, in the order to list them.</param>
    /// <param name="shown">Whether the server's own rows were asked for.</param>
    /// <param name="isSystem">Whether a row is the server's own.</param>
    /// <param name="alertName">The name an alert would give a row, or null when no alert can be about it alone.</param>
    /// <returns>The rows to list, in their order, and what was left out.</returns>
    public static (List<T> Rows, HiddenRowsResponse Hidden) Keep<T>(
        IEnumerable<T> rows,
        bool shown,
        Func<T, bool> isSystem,
        Func<T, string?> alertName)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(isSystem);
        ArgumentNullException.ThrowIfNull(alertName);

        if (shown)
        {
            return (rows.ToList(), HiddenRowsResponse.None);
        }

        var kept = new List<T>();
        var hidden = new List<T>();

        foreach (var row in rows)
        {
            (isSystem(row) ? hidden : kept).Add(row);
        }

        var names = hidden
            .Select(alertName)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(_maxNames)
            .ToList();

        return (kept, hidden.Count == 0 ? HiddenRowsResponse.None : new HiddenRowsResponse(hidden.Count, names));
    }

    /// <summary>The name an alert gives a row of a system database: the database, if it is the engine's own.</summary>
    /// <param name="engine">The engine, as <c>db.system.name</c> spells it.</param>
    /// <param name="database">The row's database.</param>
    /// <returns>The database, or null when it is the applications' and so says nothing about the hidden row.</returns>
    public static string? SystemDatabase(string engine, string? database) =>
        SystemObjects.IsSystemDatabase(engine, database) ? database : null;
}
