using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.SqlServers;
using Veyra.Testing;

namespace Veyra.Agents.Tests.SqlServers;

[Collection(PostgresGroup.Name)]
public class SqlServerServiceTests(PostgresFixture postgres)
{
    private const string _password = "Correct-Horse-7";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private readonly EphemeralDataProtectionProvider _protection = new();

    [Fact]
    public async Task AddAsync_NoAgentChosen_GivesTheServerToTheBuiltInAgent()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);

        var server = await Service(database).AddAsync(Input(), CancellationToken.None);

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

        await service.AddAsync(Input(), CancellationToken.None);

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
        await service.AddAsync(Input(), CancellationToken.None);

        await Assert.ThrowsAsync<AgentsConflictException>(() =>
            service.AddAsync(Input() with { Address = "10.0.0.9" }, CancellationToken.None));
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

        var server = await Service(database).AddAsync(Input() with { Address = address }, CancellationToken.None);

        Assert.Equal(address, server.Address);
    }

    [Fact]
    public async Task AddAsync_NoBuiltInAgent_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            Service(database).AddAsync(Input(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_NoPassword_KeepsTheStoredOne()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var changed = await service.UpdateAsync(
            added.Id,
            Input() with { Address = "10.0.0.6", Password = null, Encryption = SqlServerEncryption.Off },
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
    public async Task UpdateAsync_UnknownServer_ReturnsNull()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        Assert.Null(await Service(database).UpdateAsync(Guid.CreateVersion7(), Input(), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_Server_IsNoLongerListed()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        var added = await service.AddAsync(Input(), CancellationToken.None);

        Assert.True(await service.RemoveAsync(added.Id, CancellationToken.None));

        Assert.Empty(await service.ListAsync(CancellationToken.None));
        Assert.False(await service.RemoveAsync(added.Id, CancellationToken.None));
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
            NullLogger<SqlServerService>.Instance);

    private static async Task<bool> StoredTextContainsAsync(NpgsqlDataSource database, string text)
    {
        await using var command = database.CreateCommand(
            "SELECT count(*) FROM agents.sql_server AS s WHERE s::text LIKE '%' || $1 || '%'");
        command.Parameters.AddWithValue(text);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }
}
