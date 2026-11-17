using System.Security.Claims;
using Veyra.Agents;
using Veyra.Agents.Agents;
using Veyra.Core.Settings;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Agents;

[Collection(PostgresGroup.Name)]
public class AgentHostServiceTests(PostgresFixture postgres)
{
    private static readonly HostWatch _everything = new(Host: true, EventLog: true, Iis: true, Environment: "staging");

    private readonly FakeMonitoringHistory _history = new();

    private readonly ClaimsPrincipal _by = FakeMonitoringHistory.Admin;

    [Fact]
    public async Task SetWatchAsync_WindowsAgent_StoresAndReadsBackEverything()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");

        var saved = await new AgentHostService(store, _history).SetWatchAsync(agent.Id, _everything, _by, CancellationToken.None);

        Assert.Equal(_everything, saved!.Watch);
        Assert.Equal(_everything, (await store.FindAsync(agent.Id, CancellationToken.None))!.Watch);
    }

    [Fact]
    public async Task SetWatchAsync_NewAgent_DoesNotWatchItsHost()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");

        Assert.Equal(HostWatch.None, (await store.FindAsync(agent.Id, CancellationToken.None))!.Watch);
    }

    [Fact]
    public async Task SetWatchAsync_UnknownAgent_ReturnsNull()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        var saved = await new AgentHostService(new AgentStore(database), _history)
            .SetWatchAsync(Guid.NewGuid(), _everything, _by, CancellationToken.None);

        Assert.Null(saved);
    }

    [Fact]
    public async Task SetWatchAsync_BuiltInAgent_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = AgentsDatabase.BuiltIn();
        await store.InsertAsync(agent, CancellationToken.None);

        var error = await Assert.ThrowsAsync<AgentsValidationException>(() =>
            new AgentHostService(store, _history).SetWatchAsync(
                agent.Id,
                new HostWatch(true, false, false, "production"),
                _by,
                CancellationToken.None));

        Assert.Equal("The built-in agent runs in a container; install an agent on the host to watch it.", error.Message);
    }

    [Fact]
    public async Task SetWatchAsync_AgentNotInstalledYet_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, null, installed: false);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            new AgentHostService(store, _history).SetWatchAsync(agent.Id, _everything, _by, CancellationToken.None));
    }

    [Theory]
    [InlineData("Ubuntu 24.04.1 LTS", true, false)]
    [InlineData("Ubuntu 24.04.1 LTS", false, true)]
    [InlineData(null, true, false)]
    public async Task SetWatchAsync_EventLogOrIisOffWindows_IsRefused(string? os, bool eventLog, bool iis)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, os);

        var error = await Assert.ThrowsAsync<AgentsValidationException>(() =>
            new AgentHostService(store, _history).SetWatchAsync(
                agent.Id,
                new HostWatch(true, eventLog, iis, "production"),
                _by,
                CancellationToken.None));

        Assert.Equal("The Event Log and IIS are read on Windows only.", error.Message);
    }

    [Fact]
    public async Task SetWatchAsync_LinuxAgentWatchingItsHost_IsAccepted()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Ubuntu 24.04.1 LTS");

        var saved = await new AgentHostService(store, _history).SetWatchAsync(
            agent.Id,
            new HostWatch(true, false, false, " production "),
            _by,
            CancellationToken.None);

        Assert.Equal(new HostWatch(true, false, false, "production"), saved!.Watch);
    }

    [Fact]
    public async Task SetWatchAsync_LinuxAgentWatchingDocker_StoresAndReadsItBack()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Ubuntu 24.04.1 LTS");
        var docker = new HostWatch(true, false, false, "production", Docker: true);

        var saved = await new AgentHostService(store, _history).SetWatchAsync(agent.Id, docker, _by, CancellationToken.None);

        Assert.Equal(docker, saved!.Watch);
        Assert.Equal(docker, (await store.FindAsync(agent.Id, CancellationToken.None))!.Watch);
    }

    [Fact]
    public async Task SetWatchAsync_DockerOnWindows_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");

        var error = await Assert.ThrowsAsync<AgentsValidationException>(() =>
            new AgentHostService(store, _history).SetWatchAsync(
                agent.Id,
                new HostWatch(true, false, false, "production", Docker: true),
                _by,
                CancellationToken.None));

        Assert.Equal("Docker containers are read on Linux only.", error.Message);
    }

    [Fact]
    public async Task SetWatchAsync_HostOff_TurnsDockerOff()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Ubuntu 24.04.1 LTS");
        var service = new AgentHostService(store, _history);
        await service.SetWatchAsync(
            agent.Id,
            new HostWatch(true, false, false, "production", Docker: true),
            _by,
            CancellationToken.None);

        var saved = await service.SetWatchAsync(
            agent.Id,
            new HostWatch(false, false, false, "production", Docker: true),
            _by,
            CancellationToken.None);

        Assert.Equal(new HostWatch(false, false, false, "production"), saved!.Watch);
    }

    [Fact]
    public async Task SetWatchAsync_HostOff_TurnsEventLogAndIisOff()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");
        var service = new AgentHostService(store, _history);
        await service.SetWatchAsync(agent.Id, _everything, _by, CancellationToken.None);

        var saved = await service.SetWatchAsync(
            agent.Id,
            _everything with { Host = false },
            _by,
            CancellationToken.None);

        Assert.Equal(new HostWatch(false, false, false, "staging"), saved!.Watch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("-leading")]
    public async Task SetWatchAsync_BadEnvironment_IsRefused(string environment)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            new AgentHostService(store, _history).SetWatchAsync(
                agent.Id,
                new HostWatch(true, false, false, environment),
                _by,
                CancellationToken.None));
    }

    [Fact]
    public async Task SetWatchAsync_TooLongEnvironment_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            new AgentHostService(store, _history).SetWatchAsync(
                agent.Id,
                new HostWatch(true, false, false, new string('a', 65)),
                _by,
                CancellationToken.None));
    }

    [Fact]
    public async Task SetWatchAsync_Change_NamesWhatTheAgentNowReadsOrNotInTheHistory()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var store = new AgentStore(database);
        var agent = await InsertRemoteAsync(store, "Windows Server 2022 Datacenter");
        var service = new AgentHostService(store, _history);

        await service.SetWatchAsync(agent.Id, _everything, _by, CancellationToken.None);
        await service.SetWatchAsync(agent.Id, _everything, _by, CancellationToken.None);
        await service.SetWatchAsync(agent.Id, _everything with { Iis = false }, _by, CancellationToken.None);

        Assert.Equal(
            ["host, Event Log, IIS, environment", "IIS"],
            _history.Changes.Select(entry => string.Join(", ", entry.Change.Fields)));
        Assert.All(
            _history.Changes,
            entry => Assert.Equal(
                (MonitoringSubject.Agent, agent.Name, MonitoringChangeKind.Changed),
                (entry.Change.Subject, entry.Change.Name, entry.Change.Kind)));
    }

    private static async Task<Agent> InsertRemoteAsync(AgentStore store, string? os, bool installed = true)
    {
        var agent = AgentsDatabase.BuiltIn() with
        {
            Name = "Web server",
            Placement = AgentPlacement.Remote,
            IngestionKeyId = installed ? Guid.CreateVersion7() : null,
        };
        await store.InsertAsync(agent, CancellationToken.None);
        await store.SaveReportAsync(agent.Id, AgentReport.None with { OperatingSystem = os }, CancellationToken.None);

        return agent;
    }
}
