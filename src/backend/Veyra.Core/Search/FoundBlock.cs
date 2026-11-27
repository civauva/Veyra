using Veyra.Core.Alerts;

namespace Veyra.Core.Search;

/// <summary>A service, host, container, database server, or database whose name matched a search.</summary>
/// <param name="Kind">The kind of block.</param>
/// <param name="Name">
/// The block name; for a database server and its databases, the engine and server, such as
/// <c>microsoft.sql_server sql-prod-01</c> (ADR 0018).
/// </param>
/// <param name="Database">
/// The database, for a database of a server; <see langword="null"/> for the block itself.
/// </param>
/// <param name="Records">The block's log records in the window, from the hourly counts; 0 for a database.</param>
public sealed record FoundBlock(AlertBlockKind Kind, string Name, string? Database, long Records);
