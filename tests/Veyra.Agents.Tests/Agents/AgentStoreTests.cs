using Veyra.Agents.Agents;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Agents;

[Collection(PostgresGroup.Name)]
public class AgentStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindByKeyAsync_InsertedAgent_IsFoundWithoutAReport()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = AgentsDatabase.BuiltIn();

        await store.InsertAsync(agent, CancellationToken.None);

        var found = await store.FindByKeyAsync(agent.IngestionKeyId!.Value, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(agent.Id, found.Id);
        Assert.Equal(AgentPlacement.BuiltIn, found.Placement);
        Assert.Null(found.Report.LastSeenAt);
    }

    [Fact]
    public async Task SaveReportAsync_Report_IsReadBack()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = AgentsDatabase.BuiltIn();
        await store.InsertAsync(agent, CancellationToken.None);
        var report = new AgentReport(
            InstanceUid: [1, 2, 3],
            Version: "0.140.1",
            HostName: "veyra-agent",
            OperatingSystem: "Alpine 3.22.2",
            Healthy: true,
            HealthError: null,
            ConfigHash: [9, 9],
            ConfigState: AgentConfigState.Failed,
            ConfigError: "unknown receiver",
            LastSeenAt: _noon);

        await store.SaveReportAsync(agent.Id, report, CancellationToken.None);

        var saved = Assert.Single(await store.ListAsync(CancellationToken.None)).Report;
        Assert.Equal(report.InstanceUid, saved.InstanceUid);
        Assert.Equal(report.ConfigHash, saved.ConfigHash);
        Assert.Equal(report with { InstanceUid = saved.InstanceUid, ConfigHash = saved.ConfigHash }, saved);
    }

    [Fact]
    public async Task RevokeAsync_Agent_IsNoLongerFoundOrListed()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = AgentsDatabase.BuiltIn();
        await store.InsertAsync(agent, CancellationToken.None);

        await store.RevokeAsync(agent.Id, _noon, CancellationToken.None);

        Assert.Null(await store.FindByKeyAsync(agent.IngestionKeyId!.Value, CancellationToken.None));
        Assert.Null(await store.FindBuiltInAsync(CancellationToken.None));
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceKeyAsync_NewKey_FindsTheAgentByItOnly()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = AgentsDatabase.BuiltIn();
        await store.InsertAsync(agent, CancellationToken.None);
        var newKey = Guid.CreateVersion7();

        await store.ReplaceKeyAsync(agent.Id, newKey, CancellationToken.None);

        Assert.Null(await store.FindByKeyAsync(agent.IngestionKeyId!.Value, CancellationToken.None));
        Assert.Equal(agent.Id, (await store.FindByKeyAsync(newKey, CancellationToken.None))?.Id);
        Assert.Equal(newKey, (await store.FindBuiltInAsync(CancellationToken.None))?.IngestionKeyId);
    }
}
