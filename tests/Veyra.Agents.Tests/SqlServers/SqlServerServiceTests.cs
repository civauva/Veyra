using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.SqlServers;
using Veyra.Core.Settings;
using Veyra.Testing;

namespace Veyra.Agents.Tests.SqlServers;

[Collection(PostgresGroup.Name)]
public class SqlServerServiceTests(PostgresFixture postgres)
{
    private const string _password = "Correct-Horse-7";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private readonly EphemeralDataProtectionProvider _protection = new();

    private readonly FakeMonitoringHistory _history = new();

    private readonly ClaimsPrincipal _by = FakeMonitoringHistory.Admin;

    [Fact]
    public async Task AddAsync_NoAgentChosen_GivesTheServerToTheBuiltInAgent()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);

        var server = await Service(database).AddAsync(Input(), _by, CancellationToken.None);

        Assert.Equal(agent.Id, server.AgentId);
        Assert.Equal("sql-prod-01", server.Name);
        Assert.Equal(_clock.GetUtcNow(), server.CreatedAt);
    }

    [Fact]
    public async Task AddAsync_Password_IsStoredEncryptedAndGivenOnlyToItsAgent()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);
        var service = Service(database);

        await service.AddAsync(Input(), _by, CancellationToken.None);

        Assert.False(await StoredTextContainsAsync(database, _password));
        var target = Assert.Single(await service.TargetsAsync(agent.Id, CancellationToken.None));
        Assert.Equal(_password, target.Password);
        Assert.Empty(await service.TargetsAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_NameInUse_IsAConflict()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        await service.AddAsync(Input(), _by, CancellationToken.None);

        await Assert.ThrowsAsync<AgentsConflictException>(() =>
            service.AddAsync(Input() with { Address = "10.0.0.9" }, _by, CancellationToken.None));
    }

    [Theory]
    [InlineData("sql prod", "10.0.0.5", 1433, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "", 1433, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "evil?encrypt=disable", 1433, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "host/instance", 1433, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "user@host", 1433, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "10.0.0.5", 0, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "10.0.0.5", 65536, "production", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "10.0.0.5", 1433, "", "veyra_monitor", _password)]
    [InlineData("sql-prod-01", "10.0.0.5", 1433, "production", "", _password)]
    [InlineData("sql-prod-01", "10.0.0.5", 1433, "production", "veyra_monitor", "")]
    public async Task AddAsync_InputBreaksARule_IsRejected(
        string name,
        string address,
        int port,
        string environment,
        string login,
        string password)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            new SqlServerInput(name, address, port, environment, null, login, password, SqlServerEncryption.Required),
            _by,
            CancellationToken.None));
    }

    [Theory]
    [InlineData("sql-prod-01.corp.local")]
    [InlineData("10.0.0.5")]
    [InlineData("[fd00::5]")]
    [InlineData("mssql")]
    public async Task AddAsync_HostNameOrAddress_IsAccepted(string address)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var server = await Service(database).AddAsync(Input() with { Address = address }, _by, CancellationToken.None);

        Assert.Equal(address, server.Address);
    }

    [Fact]
    public async Task AddAsync_NoBuiltInAgent_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            Service(database).AddAsync(Input(), _by, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_NoPassword_KeepsTheStoredOne()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var changed = await service.UpdateAsync(
            added.Id,
            Input() with { Address = "10.0.0.6", Password = null, Encryption = SqlServerEncryption.Off },
            _by,
            CancellationToken.None);

        Assert.NotNull(changed);
        Assert.Equal(added.CreatedAt, changed.CreatedAt);
        Assert.Equal(_clock.GetUtcNow(), changed.ChangedAt);
        var target = Assert.Single(await service.TargetsAsync(agent.Id, CancellationToken.None));
        Assert.Equal(_password, target.Password);
        Assert.Equal("10.0.0.6", target.Server.Address);
        Assert.Equal(SqlServerEncryption.Off, target.Server.Encryption);
    }

    [Fact]
    public async Task AddAsync_NoPlatformGiven_IsASqlServerWithoutADatabase()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var server = await Service(database).AddAsync(Input(), _by, CancellationToken.None);

        Assert.Equal(SqlServerPlatform.SqlServer, server.Platform);
        Assert.Null(server.Database);
    }

    [Fact]
    public async Task AddAsync_AzureSqlDatabase_StoresTheTrimmedDatabaseName()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);
        var service = Service(database);

        await service.AddAsync(
            Input() with { Platform = SqlServerPlatform.AzureSqlDatabase, Database = "  orders  " },
            _by,
            CancellationToken.None);

        var target = Assert.Single(await service.TargetsAsync(agent.Id, CancellationToken.None));
        Assert.Equal(SqlServerPlatform.AzureSqlDatabase, target.Server.Platform);
        Assert.Equal("orders", target.Server.Database);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_AzureSqlDatabaseWithoutDatabase_IsRejected(string? name)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var exception = await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Platform = SqlServerPlatform.AzureSqlDatabase, Database = name },
            _by,
            CancellationToken.None));

        Assert.Equal("Name the database: Azure SQL Database is read one database at a time.", exception.Message);
    }

    [Theory]
    [InlineData(SqlServerPlatform.SqlServer)]
    [InlineData(SqlServerPlatform.ManagedInstance)]
    public async Task AddAsync_OtherKindWithADatabase_IsRejected(SqlServerPlatform platform)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var exception = await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Platform = platform, Database = "orders" },
            _by,
            CancellationToken.None));

        Assert.Equal("Only an Azure SQL Database names its database.", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AddAsync_ManagedInstanceWithBlankDatabase_StoresNoDatabase(string? name)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var server = await Service(database).AddAsync(
            Input() with { Platform = SqlServerPlatform.ManagedInstance, Database = name },
            _by,
            CancellationToken.None);

        Assert.Equal(SqlServerPlatform.ManagedInstance, server.Platform);
        Assert.Null(server.Database);
    }

    [Fact]
    public async Task AddAsync_UndefinedPlatform_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var exception = await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Platform = (SqlServerPlatform)9 },
            _by,
            CancellationToken.None));

        Assert.Equal("Choose the kind of SQL Server.", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_ChangedToAzureSqlDatabase_IsReadBackFromTheStore()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, CancellationToken.None);

        await service.UpdateAsync(
            added.Id,
            Input() with { Password = null, Platform = SqlServerPlatform.AzureSqlDatabase, Database = "orders" },
            _by,
            CancellationToken.None);

        var target = Assert.Single(await service.TargetsAsync(agent.Id, CancellationToken.None));
        Assert.Equal(SqlServerPlatform.AzureSqlDatabase, target.Server.Platform);
        Assert.Equal("orders", target.Server.Database);
    }

    [Fact]
    public async Task UpdateAsync_UnknownServer_ReturnsNull()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        Assert.Null(await Service(database).UpdateAsync(Guid.CreateVersion7(), Input(), _by, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_Server_IsNoLongerListed()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, CancellationToken.None);

        Assert.True(await service.RemoveAsync(added.Id, _by, CancellationToken.None));

        Assert.Empty(await service.ListAsync(CancellationToken.None));
        Assert.False(await service.RemoveAsync(added.Id, _by, CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_Server_IsAddedToTheSettingsHistoryByWhoAddedIt()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        await Service(database).AddAsync(Input(), _by, CancellationToken.None);

        var (change, by) = Assert.Single(_history.Changes);
        Assert.Equal(
            new MonitoringChange(MonitoringSubject.SqlServer, "sql-prod-01", MonitoringChangeKind.Added, []),
            change with { Fields = [] });
        Assert.Empty(change.Fields);
        Assert.Same(_by, by);
    }

    [Fact]
    public async Task UpdateAsync_ChangedFields_AreNamedInTheHistoryWithoutTheirValues()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, CancellationToken.None);

        await service.UpdateAsync(
            added.Id,
            Input() with { Port = 1533, Password = "Another-Horse-8", Encryption = SqlServerEncryption.Off },
            _by,
            CancellationToken.None);

        var change = _history.Changes[^1].Change;
        Assert.Equal((MonitoringChangeKind.Changed, "sql-prod-01"), (change.Kind, change.Name));
        Assert.Equal(["port", "password", "encryption"], change.Fields);
    }

    [Fact]
    public async Task UpdateAsync_NothingChanged_IsNotInTheHistory()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, CancellationToken.None);

        await service.UpdateAsync(added.Id, Input() with { Password = null }, _by, CancellationToken.None);

        Assert.Single(_history.Changes);
    }

    [Fact]
    public async Task RemoveAsync_Server_IsRemovedInTheHistoryByName()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), _by, CancellationToken.None);

        await service.RemoveAsync(added.Id, _by, CancellationToken.None);
        await service.RemoveAsync(added.Id, _by, CancellationToken.None);

        Assert.Equal(
            [MonitoringChangeKind.Added, MonitoringChangeKind.Removed],
            _history.Changes.Select(entry => entry.Change.Kind));
        Assert.Equal("sql-prod-01", _history.Changes[^1].Change.Name);
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

    private static async Task<Agent> SeedBuiltInAsync(NpgsqlDataSource database)
    {
        var agent = AgentsDatabase.BuiltIn();
        await new AgentStore(database).InsertAsync(agent, CancellationToken.None);

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

    private static async Task<bool> StoredTextContainsAsync(NpgsqlDataSource database, string text)
    {
        await using var command = database.CreateCommand(
            "SELECT count(*) FROM agents.sql_server AS s WHERE s::text LIKE '%' || $1 || '%'");
        command.Parameters.AddWithValue(text);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }
}
