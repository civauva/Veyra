using Microsoft.Extensions.Time.Testing;
using Veyra.Agents.Agents;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Agents;

/// <summary>What the Overview's collector health reads of the agents (ADR 0023).</summary>
[Collection(PostgresGroup.Name)]
public class AgentStatusesTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_Agents_HaveTheStateNameLastSeenAndVersionOfTheAgentsPage()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var builtIn = AgentsDatabase.BuiltIn();
        await store.InsertAsync(builtIn, CancellationToken.None);
        await store.SaveReportAsync(
            builtIn.Id,
            AgentReport.None with { Version = "0.140.1", Healthy = true, LastSeenAt = _noon.AddSeconds(-20) },
            CancellationToken.None);
        var statuses = new AgentStatuses(store, new FakeTimeProvider(_noon));

        var agent = Assert.Single(await statuses.ListAsync(CancellationToken.None));

        Assert.Equal(
            (builtIn.Name, "Online", (DateTimeOffset?)_noon.AddSeconds(-20), "0.140.1"),
            (agent.Name, agent.State, agent.LastSeenAt, agent.Version));
    }

    [Fact]
    public async Task ListAsync_AgentThatNeverConnected_IsNotConnected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        await store.InsertAsync(AgentsDatabase.BuiltIn(), CancellationToken.None);
        var statuses = new AgentStatuses(store, new FakeTimeProvider(_noon));

        var agent = Assert.Single(await statuses.ListAsync(CancellationToken.None));

        Assert.Equal(("NotConnected", null, null), (agent.State, agent.LastSeenAt, agent.Version));
    }
}
