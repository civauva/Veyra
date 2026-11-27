using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.Probes;
using Veyra.Agents.SqlServers;
using Veyra.Core.Agents;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Probes;

[Collection(PostgresGroup.Name)]
public class ProbeServiceTests(PostgresFixture postgres)
{
    private const string _password = "Probe-Horse-9";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));

    private readonly EphemeralDataProtectionProvider _protection = new();

    private readonly FakeLogSearch _logs = new();

    [Fact]
    public async Task StartAsync_OnlineAgent_IsPendingAndKeepsThePasswordEncrypted()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);

        var probe = await service.StartAsync(Input(), default);

        Assert.Equal((ProbeState.Pending, (Guid?)agent.Id), (probe!.State, probe.AgentId));
        Assert.False(await StoredTextContainsAsync(database, _password));
        var target = Assert.Single(await service.TargetsAsync(agent.Id, default));
        Assert.Equal((probe.Id, _password, "sql-new"), (target.Id, target.Password, target.Connection.Address));
    }

    [Fact]
    public async Task StartAsync_NoBuiltInAgent_FailsAtOnceWithNoAgent()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        var probe = await Service(database).StartAsync(Input(), default);

        Assert.Equal((ProbeState.Failed, (ProbeFailure?)ProbeFailure.NoAgent), (probe!.State, probe.Failure));
        Assert.NotNull(probe.FinishedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task StartAsync_AgentNotConnectedLately_FailsAtOnceWithAgentOffline(int? minutesAgo)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, minutesAgo is { } minutes ? _clock.GetUtcNow().AddMinutes(-minutes) : null);

        var probe = await Service(database).StartAsync(Input(), default);

        Assert.Equal((ProbeState.Failed, (ProbeFailure?)ProbeFailure.AgentOffline), (probe!.State, probe.Failure));
        Assert.False(await StoredTextContainsAsync(database, _password));
    }

    [Fact]
    public async Task StartAsync_WithoutPassword_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, _clock.GetUtcNow());

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            Service(database).StartAsync(Input() with { Password = null }, default));
    }

    [Fact]
    public async Task StartAsync_SavedServerWithoutConnection_TestsItsStoredConnectionAndPassword()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedAgentAsync(database, _clock.GetUtcNow());
        var server = await AddServerAsync(database);
        var service = Service(database);

        var probe = await service.StartAsync(new ProbeInput(ServerId: server.Id), default);

        Assert.Equal(server.Id, probe!.ServerId);
        var target = Assert.Single(await service.TargetsAsync(agent.Id, default));
        Assert.Equal(("10.0.0.5", "Server-Horse-1"), (target.Connection.Address, target.Password));
        Assert.Equal(probe.Id, (await service.LatestForServerAsync(server.Id, default))!.Id);
    }

    [Fact]
    public async Task StartAsync_SavedServerWithNewAddressAndNoPassword_UsesTheStoredPassword()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedAgentAsync(database, _clock.GetUtcNow());
        var server = await AddServerAsync(database);
        var service = Service(database);

        await service.StartAsync(Input() with { ServerId = server.Id, Password = null }, default);

        var target = Assert.Single(await service.TargetsAsync(agent.Id, default));
        Assert.Equal(("sql-new", "Server-Horse-1"), (target.Connection.Address, target.Password));
    }

    [Fact]
    public async Task StartAsync_UnknownServer_IsNotFound()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, _clock.GetUtcNow());

        Assert.Null(await Service(database).StartAsync(new ProbeInput(ServerId: Guid.CreateVersion7()), default));
    }

    [Fact]
    public async Task CompleteAsync_AnswerFromItsAgent_SucceedsWithTheResultAndForgetsThePassword()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);
        var probe = await service.StartAsync(Input(), default);

        var completed = await service.CompleteAsync(Answer(probe!.Id, agent.IngestionKeyId), default);
        var again = await service.CompleteAsync(Answer(probe.Id, agent.IngestionKeyId), default);

        Assert.True(completed);
        Assert.False(again);
        var read = await service.GetAsync(probe.Id, default);
        Assert.Equal(ProbeState.Succeeded, read!.State);
        Assert.Equal(
            ("16.0.4135.4", "CU14", "Developer Edition (64-bit)", "sqlserver", 7),
            (read.Result!.Version, read.Result.UpdateLevel, read.Result.Edition, read.Result.Platform,
                read.Result.Databases));
        Assert.Equal(["master", "shop"], read.Result.DatabaseNames);
        Assert.Equal(["VIEW SERVER STATE"], read.Result.MissingPermissions);
        Assert.False(await StoredTextContainsAsync(database, "password\":\""));
        Assert.Empty(await service.TargetsAsync(agent.Id, default));
        Assert.False(await PasswordKeptAsync(database, probe.Id));
    }

    [Fact]
    public async Task CompleteAsync_AnswerWithAnotherKey_IsIgnored()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);
        var probe = await service.StartAsync(Input(), default);

        Assert.False(await service.CompleteAsync(Answer(probe!.Id, Guid.CreateVersion7()), default));
        Assert.Equal(ProbeState.Pending, (await service.GetAsync(probe.Id, default))!.State);
    }

    [Theory]
    [InlineData("mssql: login error: Login failed for user 'veyra_monitor'.", ProbeFailure.LoginFailed)]
    [InlineData("unable to open tcp connection with host: dial tcp: i/o timeout", ProbeFailure.Unreachable)]
    [InlineData("TLS Handshake failed: x509: certificate signed by unknown authority", ProbeFailure.Certificate)]
    [InlineData("no instance matching 'REPORTS' returned from host 'sql-new'", ProbeFailure.Unreachable)]
    [InlineData("something else broke", ProbeFailure.Other)]
    public async Task GetAsync_ReaderErrorOfTheProbe_FailsItWithTheReason(string error, ProbeFailure failure)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);
        var probe = await service.StartAsync(Input(), default);
        _clock.Advance(TimeSpan.FromSeconds(20));
        _logs.AddReaderError($"sqlquery/probe/{probe!.Id:N}", error, _clock.GetUtcNow());

        var read = await service.GetAsync(probe.Id, default);

        Assert.Equal((ProbeState.Failed, (ProbeFailure?)failure, error), (read!.State, read.Failure, read.Message));
        Assert.Empty(await service.TargetsAsync(agent.Id, default));
        Assert.False(await PasswordKeptAsync(database, probe.Id));
    }

    [Fact]
    public async Task GetAsync_AnotherServersError_LeavesTheProbePending()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);
        var probe = await service.StartAsync(Input(), default);
        _logs.AddReaderError($"sqlquery/{Guid.CreateVersion7():N}", "Login failed", _clock.GetUtcNow());

        Assert.Equal(ProbeState.Pending, (await service.GetAsync(probe!.Id, default))!.State);
    }

    [Fact]
    public async Task SweepAsync_NoAnswerForTwoMinutes_TimesOutAndForgetsThePassword()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);
        var probe = await service.StartAsync(Input(), default);
        _clock.Advance(ProbeService.Timeout);

        await service.SweepAsync(default);

        var read = await new ProbeStore(database).FindAsync(probe!.Id, default);
        Assert.Equal((ProbeState.Failed, (ProbeFailure?)ProbeFailure.TimedOut), (read!.State, read.Failure));
        Assert.False(await PasswordKeptAsync(database, probe.Id));
    }

    [Fact]
    public async Task SweepAsync_ProbeADayOld_IsDeleted()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, _clock.GetUtcNow());
        var service = Service(database);
        var probe = await service.StartAsync(Input(), default);
        _clock.Advance(ProbeService.KeptFor + TimeSpan.FromMinutes(1));

        await service.SweepAsync(default);

        Assert.Null(await service.GetAsync(probe!.Id, default));
    }

    [Fact]
    public void Result_MalformedColumns_ReadAsEmpty()
    {
        var result = ProbeService.Result(new Dictionary<string, string>
        {
            ["databases"] = "many",
            ["database_names"] = "not json",
        });

        Assert.Equal((0, 0, 0), (result.Databases, result.DatabaseNames.Count, result.MissingPermissions.Count));
        Assert.Null(result.Version);
    }

    [Fact]
    public void ProbeReports_Full_RefusesTheAnswerSoTheAgentRetries()
    {
        var reports = new ProbeReports();
        var answer = Answer(Guid.CreateVersion7(), null);

        for (var i = 0; i < ProbeReports.Capacity; i++)
        {
            Assert.True(reports.TryAccept(answer));
        }

        Assert.False(reports.TryAccept(answer));
    }

    private static ProbeInput Input() =>
        new(Address: "sql-new", Login: "veyra_monitor", Password: _password, Encryption: SqlServerEncryption.Off);

    private AgentProbeReport Answer(Guid probeId, Guid? keyId) =>
        new(
            keyId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["veyra_record"] = AgentProbeReport.Kind,
                ["probe"] = probeId.ToString("N"),
                ["version"] = "16.0.4135.4",
                ["update_level"] = "CU14",
                ["edition"] = "Developer Edition (64-bit)",
                ["platform"] = "sqlserver",
                ["databases"] = "7",
                ["database_names"] = """[{"name":"master"},{"name":"shop"}]""",
                ["missing"] = """[{"permission":"VIEW SERVER STATE"}]""",
            },
            _clock.GetUtcNow());

    private static async Task<Agent> SeedAgentAsync(NpgsqlDataSource database, DateTimeOffset? lastSeen)
    {
        var agent = AgentsDatabase.BuiltIn() with { Report = AgentReport.None with { LastSeenAt = lastSeen } };
        var store = new AgentStore(database);
        await store.InsertAsync(agent, default);
        await store.SaveReportAsync(agent.Id, agent.Report, default);

        return agent;
    }

    private async Task<SqlServer> AddServerAsync(NpgsqlDataSource database) =>
        await Servers(database).AddAsync(
            new SqlServerInput(
                "sql-prod-01",
                "10.0.0.5",
                1433,
                "production",
                null,
                "veyra_monitor",
                "Server-Horse-1",
                SqlServerEncryption.Required),
            FakeMonitoringHistory.Admin,
            default);

    private SqlServerService Servers(NpgsqlDataSource database) =>
        new(
            new SqlServerStore(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            _clock,
            new FakeMonitoringHistory(),
            NullLogger<SqlServerService>.Instance);

    private ProbeService Service(NpgsqlDataSource database) =>
        new(
            new ProbeStore(database),
            Servers(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            new SqlServerSignals(_logs, new FakeMetricSearch()),
            _clock,
            NullLogger<ProbeService>.Instance);

    private static async Task<bool> StoredTextContainsAsync(NpgsqlDataSource database, string text)
    {
        await using var command = database.CreateCommand(
            "SELECT count(*) FROM agents.sql_probe AS p WHERE p::text LIKE '%' || $1 || '%'");
        command.Parameters.AddWithValue(text);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<bool> PasswordKeptAsync(NpgsqlDataSource database, Guid id)
    {
        await using var command = database.CreateCommand(
            "SELECT password IS NOT NULL FROM agents.sql_probe WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
