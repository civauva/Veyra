using System.Text.Json.Serialization;
using Veyra.Query.Home;

namespace Veyra.Query.Search.Api;

/// <summary>A service, host, container, database server, or database a search found.</summary>
/// <param name="Kind">
/// <c>service</c>, <c>host</c>, <c>container</c>, <c>server</c> (a database server), or <c>database</c>.
/// </param>
/// <param name="Name">
/// Its name; for a database server its server, such as <c>SQL-PROD-01</c>, and for a database its own name.
/// </param>
/// <param name="Health">
/// Its state from its open alerts (ADR 0007); <see langword="null"/> when the group ran out of time before the alerts
/// were read.
/// </param>
/// <param name="Detail">
/// A short figure, such as <c>742 ms p95</c>, <c>disk 81%</c>, or <c>22 GB</c>; <see langword="null"/> when there is
/// none to show.
/// </param>
public sealed record SearchItemResponse(string Kind, string Name, BlockHealth? Health, string? Detail)
{
    /// <summary>For a database server or database, its engine, such as <c>microsoft.sql_server</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Engine { get; init; }

    /// <summary>For a database server or database, the server, such as <c>SQL-PROD-01</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Server { get; init; }
}
