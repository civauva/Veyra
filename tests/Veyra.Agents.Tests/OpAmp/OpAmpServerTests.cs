using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using OpAmp.Proto.V1;
using Veyra.Agents.Agents;
using Veyra.Agents.OpAmp;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.Probes;
using Veyra.Agents.SqlServers;
using Veyra.Testing;

namespace Veyra.Agents.Tests.OpAmp;

[Collection(PostgresGroup.Name)]
public class OpAmpServerTests(PostgresFixture postgres)
{
    private static readonly Uri _veyra = new("http://veyra:8080");

    private static readonly ByteString _uid =
        ByteString.CopyFrom(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());

    private readonly FakeKeys _keys = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private readonly EphemeralDataProtectionProvider _protection = new();

    [Fact]
    public async Task HandleAsync_UnknownKey_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        var reply = await Server(database).HandleAsync("vyk_unknown", First(), _veyra, CancellationToken.None);

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleAsync_RevokedAgent_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        await new AgentStore(database).RevokeAsync(agent.Id, _clock.GetUtcNow(), CancellationToken.None);

        Assert.Null(await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_FirstContactWithoutDescription_AsksForTheFullState()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (_, key) = await SeedAgentAsync(database);

        var reply = await Server(database).HandleAsync(
            key,
            new AgentToServer { InstanceUid = _uid, SequenceNum = 7 },
            _veyra,
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal((ulong)ServerToAgentFlags.ReportFullState, reply.Flags);
        Assert.Equal(_uid, reply.InstanceUid);
    }

    [Fact]
    public async Task HandleAsync_KnownAgent_SendsItsServersConfigurationWithItsHash()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var server = await AddServerAsync(database, agent);

        var reply = await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal(0UL, reply.Flags);
        var file = reply.RemoteConfig.Config.ConfigMap[""];
        var body = file.Body.ToStringUtf8();
        Assert.Equal("text/yaml", file.ContentType);
        Assert.Contains($"sqlserver/{server.Id:N}", body, StringComparison.Ordinal);
        Assert.Contains("veyra_monitor:Correct-Horse-7@10.0.0.5:1433", body, StringComparison.Ordinal);
        Assert.Contains($"Bearer {key}", body, StringComparison.Ordinal);
        Assert.Equal(SHA256.HashData(file.Body.Span), reply.RemoteConfig.ConfigHash.ToByteArray());
    }

    [Fact]
    public async Task HandleAsync_PendingProbe_IsInTheConfigurationUntilItTimesOut()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var server = Server(database);
        await server.HandleAsync(key, First(), _veyra, CancellationToken.None);
        var probe = await Probes(database).StartAsync(
            new ProbeInput(AgentId: agent.Id, Address: "sql-new", Login: "veyra_monitor", Password: "Probe-Pass-1"),
            CancellationToken.None);

        var during = await server.HandleAsync(key, First(), _veyra, CancellationToken.None);
        _clock.Advance(ProbeService.Timeout);
        var after = await server.HandleAsync(key, First(), _veyra, CancellationToken.None);

        var body = during!.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8();
        Assert.Contains($"sqlquery/probe/{probe!.Id:N}", body, StringComparison.Ordinal);
        Assert.Contains("veyra_monitor:Probe-Pass-1@sql-new:1433", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sqlquery/probe",
            after!.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_AgentWithAPostgresServer_SendsItsReceiversWithThePassword()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var server = await PostgresServers(database).AddAsync(
            new PostgresServerInput(
                "pg-main",
                "10.0.0.7",
                5432,
                "production",
                agent.Id,
                "veyra_monitor",
                "Correct-Horse-7",
                PostgresEncryption.Required),
            FakeMonitoringHistory.Admin,
            CancellationToken.None);

        var reply = await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None);

        var body = reply!.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8();
        Assert.Contains($"postgresql/{server.Id:N}:", body, StringComparison.Ordinal);
        Assert.Contains($"sqlquery/{server.Id:N}:", body, StringComparison.Ordinal);
        Assert.Contains("endpoint: \"10.0.0.7:5432\"", body, StringComparison.Ordinal);
        Assert.Contains("password: \"Correct-Horse-7\"", body, StringComparison.Ordinal);
        Assert.Contains("value: \"pg-main\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_KnownAgent_AsksItToSendItsOwnLogsToVeyra()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (_, key) = await SeedAgentAsync(database);

        var reply = await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None);

        var ownLogs = reply!.ConnectionSettings.OwnLogs;
        Assert.Equal("http://veyra:8080/v1/logs", ownLogs.DestinationEndpoint);
        var header = Assert.Single(ownLogs.Headers.Headers_);
        Assert.Equal("Authorization", header.Key);
        Assert.Equal($"Bearer {key}", header.Value);
        Assert.False(reply.ConnectionSettings.Hash.IsEmpty);
        Assert.True((reply.Capabilities & (ulong)ServerCapabilities.OffersConnectionSettings) != 0);
        Assert.True((reply.Capabilities & (ulong)ServerCapabilities.OffersRemoteConfig) != 0);
    }

    [Fact]
    public async Task HandleAsync_Report_IsStoredAndMergedWithTheNext()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var server = Server(database);
        await server.HandleAsync(key, First(), _veyra, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(30));

        // OpAMP sends only what changed: here the configuration's status, nothing about the agent itself.
        await server.HandleAsync(
            key,
            new AgentToServer
            {
                InstanceUid = _uid,
                SequenceNum = 2,
                RemoteConfigStatus = new RemoteConfigStatus
                {
                    LastRemoteConfigHash = ByteString.CopyFrom(9, 9),
                    Status = RemoteConfigStatuses.Failed,
                    ErrorMessage = "unknown type: \"sqlserverx\"",
                },
            },
            _veyra,
            CancellationToken.None);

        var report = (await new AgentStore(database).FindAsync(agent.Id, CancellationToken.None))!.Report;
        Assert.Equal("0.140.1", report.Version);
        Assert.Equal("veyra-agent", report.HostName);
        Assert.Equal("Alpine 3.22.2", report.OperatingSystem);
        Assert.True(report.Healthy);
        Assert.Equal(_uid.ToByteArray(), report.InstanceUid);
        Assert.Equal(AgentConfigState.Failed, report.ConfigState);
        Assert.Equal("unknown type: \"sqlserverx\"", report.ConfigError);
        Assert.Equal(new byte[] { 9, 9 }, report.ConfigHash);
        Assert.Equal(_clock.GetUtcNow(), report.LastSeenAt);
    }

    [Fact]
    public async Task HandleAsync_HealthWithStartTime_KeepsWhenTheCollectorStarted()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var started = new DateTimeOffset(2026, 11, 26, 6, 0, 0, TimeSpan.Zero);
        var message = First();
        message.Health = new ComponentHealth
        {
            Healthy = true,
            StartTimeUnixNano = (ulong)(started.ToUnixTimeMilliseconds() * 1_000_000),
        };

        await Server(database).HandleAsync(key, message, _veyra, CancellationToken.None);

        var report = (await new AgentStore(database).FindAsync(agent.Id, CancellationToken.None))!.Report;
        Assert.Equal(started, report.StartedAt);
    }

    [Fact]
    public async Task HandleAsync_UnhealthyComponent_KeepsItsError()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var message = First();
        message.Health = new ComponentHealth
        {
            Healthy = false,
            ComponentHealthMap =
            {
                ["pipeline:metrics"] = new ComponentHealth
                {
                    Healthy = false,
                    ComponentHealthMap =
                    {
                        ["receiver:sqlserver/a"] = new ComponentHealth { Healthy = false, LastError = "bind failed" },
                    },
                },
            },
        };

        await Server(database).HandleAsync(key, message, _veyra, CancellationToken.None);

        var report = (await new AgentStore(database).FindAsync(agent.Id, CancellationToken.None))!.Report;
        Assert.False(report.Healthy);
        Assert.Equal("bind failed", report.HealthError);
    }

    [Fact]
    public async Task HandleAsync_AgentSwitchedToWatchItsHost_GetsAnotherConfigurationHash()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        var before = await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None);

        await new AgentStore(database).SaveWatchAsync(
            agent.Id,
            new HostWatch(Host: true, EventLog: false, Iis: false, Environment: "staging"),
            CancellationToken.None);
        var after = await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None);

        Assert.NotEqual(before!.RemoteConfig.ConfigHash, after!.RemoteConfig.ConfigHash);
        Assert.Contains("staging", after.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_LinuxAgentWatchingDocker_IsToldToReadTheContainers()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var (agent, key) = await SeedAgentAsync(database);
        await new AgentStore(database).SaveWatchAsync(
            agent.Id,
            new HostWatch(Host: true, EventLog: false, Iis: false, Environment: "production", Docker: true),
            CancellationToken.None);

        var reply = await Server(database).HandleAsync(key, First(), _veyra, CancellationToken.None);

        var config = reply!.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8();
        Assert.Contains("docker_stats:", config, StringComparison.Ordinal);
        Assert.Contains("filelog/docker:", config, StringComparison.Ordinal);
    }

    private static AgentToServer First() =>
        new()
        {
            InstanceUid = _uid,
            SequenceNum = 0,
            AgentDescription = new AgentDescription
            {
                IdentifyingAttributes =
                {
                    Attribute("service.name", "otelcol-contrib"),
                    Attribute("service.version", "0.140.1"),
                },
                NonIdentifyingAttributes =
                {
                    Attribute("host.name", "veyra-agent"),
                    Attribute("os.description", "Alpine 3.22.2"),
                },
            },
            Health = new ComponentHealth { Healthy = true, Status = "StatusOK" },
        };

    private static KeyValue Attribute(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    private async Task<(Agent Agent, string Key)> SeedAgentAsync(NpgsqlDataSource database)
    {
        var issued = await _keys.IssueAsync("Agent: Built in", CancellationToken.None);
        var agent = AgentsDatabase.BuiltIn() with { IngestionKeyId = issued.KeyId };
        await new AgentStore(database).InsertAsync(agent, CancellationToken.None);

        return (agent, issued.Secret);
    }

    private async Task<SqlServer> AddServerAsync(NpgsqlDataSource database, Agent agent) =>
        await Servers(database).AddAsync(
            new SqlServerInput(
                "sql-prod-01",
                "10.0.0.5",
                1433,
                "production",
                agent.Id,
                "veyra_monitor",
                "Correct-Horse-7",
                SqlServerEncryption.Required),
            FakeMonitoringHistory.Admin,
            CancellationToken.None);

    private SqlServerService Servers(NpgsqlDataSource database) =>
        new(
            new SqlServerStore(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            _clock,
            new FakeMonitoringHistory(),
            NullLogger<SqlServerService>.Instance);

    private PostgresServerService PostgresServers(NpgsqlDataSource database) =>
        new(
            new PostgresServerStore(database),
            new AgentStore(database),
            new PostgresPasswordProtector(_protection),
            _clock,
            new FakeMonitoringHistory(),
            NullLogger<PostgresServerService>.Instance);

    private OpAmpServer Server(NpgsqlDataSource database) =>
        new(new AgentStore(database), Servers(database), PostgresServers(database), Probes(database), _keys, _clock);

    private ProbeService Probes(NpgsqlDataSource database) =>
        new(
            new ProbeStore(database),
            Servers(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            new SqlServerSignals(new FakeLogSearch(), new FakeMetricSearch()),
            _clock,
            NullLogger<ProbeService>.Instance);
}
