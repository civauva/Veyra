using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.Integrations;
using Veyra.Agents.SqlServers;
using Veyra.Core.Schema;
using Veyra.Core.Settings;
using Veyra.Testing;

namespace Veyra.Agents.Tests.SqlServers;

/// <summary>
/// What ADR 0028 adds to a SQL Server: named instances, Windows sign-in, modules, and template versions.
/// </summary>
[Collection(PostgresGroup.Name)]
public class SqlServerIntegrationTests(PostgresFixture postgres)
{
    private const string _password = "Correct-Horse-7";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));

    private readonly EphemeralDataProtectionProvider _protection = new();

    private readonly FakeMonitoringHistory _history = new();

    private readonly ClaimsPrincipal _by = FakeMonitoringHistory.Admin;

    [Fact]
    public async Task AddAsync_AddressWithInstance_SplitsItIntoAddressAndInstance()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);

        var added = await service.AddAsync(Input() with { Address = @"sql01.corp.local\SQLEXPRESS" }, _by, default);

        var stored = await service.FindAsync(added.Id, default);
        Assert.Equal(("sql01.corp.local", "SQLEXPRESS"), (stored!.Address, stored.Instance));
    }

    [Fact]
    public async Task AddAsync_SeparateInstance_IsKept()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);

        var added = await Service(database).AddAsync(Input() with { Instance = "REPORTS" }, _by, default);

        Assert.Equal(("10.0.0.5", "REPORTS"), (added.Address, added.Instance));
    }

    [Theory]
    [InlineData(@"sql01\SQLEXPRESS", "OTHER")]
    [InlineData("sql01", "bad name")]
    [InlineData("sql01", "1STARTS_WITH_A_DIGIT")]
    [InlineData("sql01", "A_NAME_LONGER_THAN_16")]
    public async Task AddAsync_InstanceBreaksARule_IsRejected(string address, string instance)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);

        await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Address = address, Instance = instance },
            _by,
            default));
    }

    [Fact]
    public async Task AddAsync_InstanceOnAzure_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);

        await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Instance = "A", Platform = SqlServerPlatform.ManagedInstance },
            _by,
            default));
    }

    [Fact]
    public async Task AddAsync_WindowsNtlm_KeepsTheDomainLogin()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);

        var added = await Service(database).AddAsync(
            Input() with { Authentication = SqlServerAuthentication.Ntlm, Login = @"CORP\veyra" },
            _by,
            default);

        Assert.Equal((SqlServerAuthentication.Ntlm, @"CORP\veyra"), (added.Authentication, added.Login));
    }

    [Theory]
    [InlineData(SqlServerAuthentication.Ntlm, "veyra")]
    [InlineData(SqlServerAuthentication.Ntlm, @"CORP\")]
    [InlineData(SqlServerAuthentication.Ntlm, @"\veyra")]
    [InlineData(SqlServerAuthentication.Sql, @"CORP\veyra")]
    public async Task AddAsync_LoginDoesNotFitTheSignIn_IsRejected(SqlServerAuthentication authentication, string login)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);

        await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Authentication = authentication, Login = login },
            _by,
            default));
    }

    [Fact]
    public async Task AddAsync_IntegratedOnAWindowsAgent_NeedsNoLoginOrPassword()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedAgentAsync(database, "Windows Server 2022 Datacenter");
        var service = Service(database);

        var added = await service.AddAsync(
            Input() with { Authentication = SqlServerAuthentication.Integrated, Login = null, Password = null },
            _by,
            default);

        Assert.Equal((SqlServerAuthentication.Integrated, ""), (added.Authentication, added.Login));
        Assert.Equal("", Assert.Single(await service.TargetsAsync(agent.Id, default)).Password);
    }

    [Theory]
    [InlineData("Alpine 3.22.2")]
    [InlineData(null)]
    public async Task AddAsync_IntegratedOnAnotherAgent_IsRejected(string? operatingSystem)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, operatingSystem);

        await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Authentication = SqlServerAuthentication.Integrated, Login = null, Password = null },
            _by,
            default));
    }

    [Fact]
    public async Task UpdateAsync_FromIntegratedToSqlWithoutPassword_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database, "Windows Server 2022 Datacenter");
        var service = Service(database);
        var added = await service.AddAsync(
            Input() with { Authentication = SqlServerAuthentication.Integrated, Login = null, Password = null },
            _by,
            default);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            service.UpdateAsync(added.Id, Input() with { Password = null }, _by, default));
    }

    [Fact]
    public async Task AddAsync_Modules_AreStoredAndReadBackWithTheRestAtTheirDefaults()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);

        var added = await service.AddAsync(
            Input() with
            {
                Modules =
                [
                    new SqlServerModuleChoice("activity", false, 10),
                    new SqlServerModuleChoice("core", true, 30),
                ],
            },
            _by,
            default);

        var stored = (await service.FindAsync(added.Id, default))!.Choices;
        Assert.Equal(SqlServerModules.All.Select(module => module.Id), stored.Select(choice => choice.Id));
        Assert.Equal(new SqlServerModuleChoice("core", true, 30), stored[0]);
        Assert.Equal(new SqlServerModuleChoice("activity", false, 10), stored[1]);
        Assert.Equal(new SqlServerModuleChoice("queries", true, 900), stored[2]);
    }

    [Theory]
    [InlineData("core", false, 15)]
    [InlineData("activity", true, 45)]
    [InlineData("activity", true, 3600)]
    [InlineData("nothing", true, 60)]
    public async Task AddAsync_ModuleChoiceBreaksARule_IsRejected(string id, bool enabled, int seconds)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);

        await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Modules = [new SqlServerModuleChoice(id, enabled, seconds)] },
            _by,
            default));
    }

    [Fact]
    public async Task UpdateAsync_ModulesLeftOut_KeepsTheStoredOnes_AndAChangeIsInTheHistory()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(
            Input() with { Modules = [new SqlServerModuleChoice("indexes", false, 3600)] },
            _by,
            default);

        var kept = await service.UpdateAsync(added.Id, Input() with { Password = null }, _by, default);
        var changed = await service.UpdateAsync(
            added.Id,
            Input() with { Password = null, Modules = [new SqlServerModuleChoice("indexes", true, 900)] },
            _by,
            default);

        Assert.False(kept!.Choices.Single(choice => choice.Id == "indexes").Enabled);
        Assert.Equal(900, changed!.Choices.Single(choice => choice.Id == "indexes").IntervalSeconds);
        Assert.Equal(["modules"], _history.Changes[^1].Change.Fields);
        Assert.Equal(2, _history.Changes.Count);
    }

    [Fact]
    public async Task AddAsync_NewServer_TakesTheCurrentVersionSetByWhoAddedIt()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);

        var added = await service.AddAsync(Input(), _by, default);

        var stored = (await service.FindAsync(added.Id, default))!;
        Assert.Equal(
            (SqlServerTemplates.Current.Version, (DateTimeOffset?)_clock.GetUtcNow(), (string?)"Lena Adler"),
            (stored.Version, stored.TemplateSetAt, stored.TemplateSetBy));
    }

    [Fact]
    public async Task SetVersionAsync_Previous_MovesTheServerAndRecordsBothVersions()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, default);
        _clock.Advance(TimeSpan.FromHours(1));

        var moved = await service.SetVersionAsync(added.Id, "0.1.0", _by, default);

        var stored = (await service.FindAsync(added.Id, default))!;
        Assert.Equal(("0.1.0", (DateTimeOffset?)_clock.GetUtcNow()), (stored.Version, stored.TemplateSetAt));
        Assert.Equal("0.1.0", moved!.Version);
        var change = _history.Changes[^1].Change;
        Assert.Equal(["version"], change.Fields);
        Assert.Equal(("0.2.0", "0.1.0"), (change.From, change.To));
    }

    [Fact]
    public async Task SetVersionAsync_SameVersion_ChangesNothing()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, default);

        await service.SetVersionAsync(added.Id, SqlServerTemplates.Current.Version, _by, default);

        Assert.Single(_history.Changes);
    }

    [Theory]
    [InlineData("0.0.1")]
    [InlineData("")]
    public async Task SetVersionAsync_VersionNotShipped_IsRejected(string version)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, default);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            service.SetVersionAsync(added.Id, version, _by, default));
    }

    [Fact]
    public async Task Migration_ServerStoredBefore_ReadsAsItDidOnTheFirstVersion()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await PostgresFixture.MigrateAsync(database, new UpTo(6));
        var agent = AgentsDatabase.BuiltIn();
        await new AgentStore(database).InsertAsync(agent, default);
        var created = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var insert = database.CreateCommand(
            """
            INSERT INTO agents.sql_server
                (id, name, address, port, environment, agent_id, login, password, encryption, created_at, changed_at)
            VALUES ($1, 'sql-old', '10.0.0.9', 1433, 'production', $2, 'veyra_monitor', 'x', 1, $3, $3)
            """))
        {
            insert.Parameters.AddWithValue(Guid.CreateVersion7());
            insert.Parameters.AddWithValue(agent.Id);
            insert.Parameters.AddWithValue(created);
            await insert.ExecuteNonQueryAsync();
        }

        await PostgresFixture.MigrateAsync(database, new AgentsMigrations());

        var (server, _) = Assert.Single(await new SqlServerStore(database).ListAsync(default));
        Assert.Equal(
            ("0.1.0", (DateTimeOffset?)created, (string?)null, SqlServerAuthentication.Sql, (string?)null),
            (server.Version, server.TemplateSetAt, server.TemplateSetBy, server.Authentication, server.Instance));
        Assert.Equal(SqlServerModules.Defaults(), server.Choices);
    }

    private static SqlServerInput Input() =>
        new(
            Name: "sql-prod-01",
            Address: "10.0.0.5",
            Port: 1433,
            Environment: "production",
            AgentId: null,
            Login: "veyra_monitor",
            Password: _password,
            Encryption: SqlServerEncryption.TrustServerCertificate);

    private static async Task<Agent> SeedAgentAsync(NpgsqlDataSource database, string? operatingSystem = null)
    {
        var agent = AgentsDatabase.BuiltIn() with
        {
            Report = AgentReport.None with { OperatingSystem = operatingSystem },
        };
        var store = new AgentStore(database);
        await store.InsertAsync(agent, CancellationToken.None);
        await store.SaveReportAsync(agent.Id, agent.Report, CancellationToken.None);

        return agent;
    }

    private SqlServerService Service(NpgsqlDataSource database) =>
        new(
            new SqlServerStore(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            _clock,
            _history,
            NullLogger<SqlServerService>.Instance);

    // The Agents module's migrations as an older Veyra had them.
    private sealed class UpTo(int version) : ISchemaMigrationSource
    {
        public IReadOnlyList<SchemaMigration> GetMigrations() =>
            new AgentsMigrations().GetMigrations().Where(migration => migration.Version <= version).ToList();
    }
}
