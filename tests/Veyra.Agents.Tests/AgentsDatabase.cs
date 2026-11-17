using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Testing;

namespace Veyra.Agents.Tests;

/// <summary>Creates a database with the Agents schema for one test, and the agents tests start from.</summary>
internal static class AgentsDatabase
{
    public static Task<NpgsqlDataSource> CreateAsync(PostgresFixture postgres) =>
        postgres.CreateMigratedDatabaseAsync(new AgentsMigrations());

    public static Agent BuiltIn() =>
        new(
            Id: Guid.CreateVersion7(),
            Name: "Built in",
            Placement: AgentPlacement.BuiltIn,
            IngestionKeyId: Guid.CreateVersion7(),
            CreatedAt: new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero),
            Report: AgentReport.None,
            Watch: HostWatch.None);
}
