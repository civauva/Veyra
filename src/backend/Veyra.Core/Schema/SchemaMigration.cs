namespace Veyra.Core.Schema;

/// <summary>
/// One forward-only SQL script that changes a module's database schema.
/// </summary>
/// <remarks>
/// Scripts are never edited once released. A change to the schema is always a new script with the next version.
/// The storage module refuses to start when an applied script was changed afterwards.
/// </remarks>
/// <param name="Module">
/// The owning module in lower case, for example <c>storage</c>. It is also the name of the module's PostgreSQL schema.
/// </param>
/// <param name="Version">The script's version within its module, starting at 1.</param>
/// <param name="Name">A short description in lower case with underscores, taken from the file name.</param>
/// <param name="Sql">The SQL to run. It runs inside a transaction together with all other pending scripts.</param>
public sealed record SchemaMigration(string Module, int Version, string Name, string Sql);
