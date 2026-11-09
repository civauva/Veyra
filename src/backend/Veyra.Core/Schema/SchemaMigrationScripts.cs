using System.Reflection;
using System.Text.RegularExpressions;

namespace Veyra.Core.Schema;

/// <summary>
/// Reads schema migration scripts that a module embeds as resources.
/// </summary>
/// <remarks>
/// A script file is named <c>V0001__create_log_record.sql</c>: a four-digit version, two underscores, and a name in
/// lower case. It lives in the module's <c>Migrations</c> folder and is marked as an embedded resource.
/// </remarks>
public static partial class SchemaMigrationScripts
{
    /// <summary>
    /// Loads every embedded <c>.sql</c> resource of an assembly as a migration of the given module.
    /// </summary>
    /// <param name="assembly">The module's assembly that embeds the scripts.</param>
    /// <param name="module">The module name in lower case, which is also its PostgreSQL schema name.</param>
    /// <returns>The module's scripts, ordered by version.</returns>
    /// <exception cref="FormatException">A script's file name does not follow the naming rule.</exception>
    /// <exception cref="InvalidOperationException">Two scripts share a version.</exception>
    public static IReadOnlyList<SchemaMigration> FromAssembly(Assembly assembly, string module)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var scripts = assembly
            .GetManifestResourceNames()
            .Where(resource => resource.EndsWith(".sql", StringComparison.Ordinal))
            .Select(resource => (resource, ReadResource(assembly, resource)));

        return Create(module, scripts);
    }

    /// <summary>
    /// Builds a module's migrations from resource names and their SQL, and checks that they are consistent.
    /// </summary>
    /// <param name="module">The module name in lower case.</param>
    /// <param name="scripts">Pairs of resource name and SQL text.</param>
    /// <returns>The migrations, ordered by version.</returns>
    internal static IReadOnlyList<SchemaMigration> Create(
        string module,
        IEnumerable<(string ResourceName, string Sql)> scripts)
    {
        // The module name becomes a PostgreSQL schema name, so it must be a plain lower-case identifier.
        if (!ModuleNamePattern().IsMatch(module))
        {
            throw new ArgumentException(
                $"Module name '{module}' must be lower case letters, digits, or underscores, starting with a letter.",
                nameof(module));
        }

        var migrations = scripts
            .Select(script =>
            {
                var (version, name) = ParseScriptName(script.ResourceName);

                return new SchemaMigration(module, version, name, script.Sql);
            })
            .OrderBy(migration => migration.Version)
            .ToList();

        var duplicate = migrations
            .GroupBy(migration => migration.Version)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Module '{module}' has more than one migration with version {duplicate.Key}.");
        }

        return migrations;
    }

    /// <summary>
    /// Extracts the version and the name from a script's resource name.
    /// </summary>
    /// <param name="resourceName">For example <c>Veyra.Storage.Migrations.V0001__create_log_record.sql</c>.</param>
    /// <returns>The version, 1 or higher, and the name.</returns>
    /// <exception cref="FormatException">The resource name does not follow the naming rule.</exception>
    internal static (int Version, string Name) ParseScriptName(string resourceName)
    {
        var match = ScriptNamePattern().Match(resourceName);

        if (!match.Success)
        {
            throw new FormatException(
                $"Migration script '{resourceName}' must be named like V0001__create_log_record.sql.");
        }

        var version = int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture);

        if (version < 1)
        {
            throw new FormatException($"Migration script '{resourceName}' must have a version of 1 or higher.");
        }

        return (version, match.Groups["name"].Value);
    }

    private static string ReadResource(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    // The file name part after the last folder dot: V + four digits + __ + lower-case name + .sql.
    [GeneratedRegex(@"(?:^|\.)V(?<version>\d{4})__(?<name>[a-z0-9_]+)\.sql$")]
    private static partial Regex ScriptNamePattern();

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex ModuleNamePattern();
}
