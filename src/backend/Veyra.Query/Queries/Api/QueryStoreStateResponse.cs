namespace Veyra.Query.Queries.Api;

/// <summary>Whether a database keeps Query Store, with the statement to turn it on (ADR 0020, area 4).</summary>
/// <param name="Database">The database.</param>
/// <param name="On">Whether Query Store is on.</param>
/// <param name="Script">The statement a DBA runs to turn it on, or <see langword="null"/> when it is on.</param>
public sealed record QueryStoreStateResponse(string Database, bool On, string? Script);
