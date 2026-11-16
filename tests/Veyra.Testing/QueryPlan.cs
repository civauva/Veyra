using System.Text.Json;
using Npgsql;

namespace Veyra.Testing;

/// <summary>
/// What PostgreSQL plans to read for a statement, from <c>EXPLAIN</c>. Plan tests use it to prove that a query reads
/// the tables, partitions, and indexes it should (ADR 0015), so a change that loses an index fails the build.
/// </summary>
/// <param name="Relations">Every table or partition the plan reads.</param>
/// <param name="Indexes">Every index the plan reads.</param>
public sealed record QueryPlan(IReadOnlyList<string> Relations, IReadOnlyList<string> Indexes)
{
    /// <summary>Explains a command with its parameters, without running it.</summary>
    /// <param name="dataSource">The database to plan against.</param>
    /// <param name="command">The command, as the code under test built it.</param>
    /// <returns>The relations and indexes of the plan.</returns>
    public static Task<QueryPlan> ExplainAsync(NpgsqlDataSource dataSource, NpgsqlCommand command) =>
        ExplainAsync(dataSource, command, []);

    /// <summary>Explains a command with its parameters, without running it.</summary>
    /// <param name="dataSource">The database to plan against.</param>
    /// <param name="command">The command, as the code under test built it.</param>
    /// <param name="settings">
    /// Planner settings for this plan only, such as the ones the code under test sets for its own statement, or
    /// <c>enable_seqscan = off</c> to plan as for tables too large to read whole: a test's few rows fit on a page, so
    /// PostgreSQL would read them all and no index would show.
    /// </param>
    /// <returns>The relations and indexes of the plan.</returns>
    public static async Task<QueryPlan> ExplainAsync(
        NpgsqlDataSource dataSource,
        NpgsqlCommand command,
        IReadOnlyList<(string Name, string Value)> settings)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var (name, value) in settings ?? [])
        {
            await using var setting = new NpgsqlCommand("SELECT set_config($1, $2, true)", connection, transaction);
            setting.Parameters.AddWithValue(name);
            setting.Parameters.AddWithValue(value);
            await setting.ExecuteNonQueryAsync();
        }

        await using var explain = new NpgsqlCommand(
            "EXPLAIN (FORMAT JSON) " + command.CommandText,
            connection,
            transaction);

        foreach (NpgsqlParameter parameter in command.Parameters)
        {
            explain.Parameters.Add(parameter.Clone());
        }

        var json = (string)(await explain.ExecuteScalarAsync())!;
        using var document = JsonDocument.Parse(json);
        var relations = new List<string>();
        var indexes = new List<string>();

        Walk(document.RootElement[0].GetProperty("Plan"), relations, indexes);

        return new QueryPlan(relations, indexes);
    }

    private static void Walk(JsonElement node, List<string> relations, List<string> indexes)
    {
        if (node.TryGetProperty("Relation Name", out var relation))
        {
            relations.Add(relation.GetString()!);
        }

        if (node.TryGetProperty("Index Name", out var index))
        {
            indexes.Add(index.GetString()!);
        }

        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, relations, indexes);
            }
        }
    }
}
