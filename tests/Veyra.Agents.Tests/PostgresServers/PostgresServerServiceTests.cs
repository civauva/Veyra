using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.PostgresServers;
using Veyra.Core.Settings;
using Veyra.Testing;

namespace Veyra.Agents.Tests.PostgresServers;

[Collection(PostgresGroup.Name)]
public class PostgresServerServiceTests(PostgresFixture postgres)
{
    private const string _password = "Correct-Horse-7";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));

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
        Assert.Equal("pg-main", server.Name);
        Assert.Equal(PostgresEncryption.TrustServerCertificate, server.Encryption);
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
    public async Task AddAsync_PasswordProtector_IsNotTheSqlServerOne()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        await Service(database).AddAsync(Input(), _by, CancellationToken.None);

        await using var command = database.CreateCommand("SELECT password FROM agents.postgres_server");
        var stored = (string)(await command.ExecuteScalarAsync())!;

        Assert.Null(new Veyra.Agents.SqlServers.SqlServerPasswordProtector(_protection).Unprotect(stored));
    }

    [Fact]
    public async Task AddAsync_NameInUse_IsAConflict()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);
        await service.AddAsync(Input(), _by, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AgentsConflictException>(() =>
            service.AddAsync(Input() with { Address = "10.0.0.9" }, _by, CancellationToken.None));

        Assert.Equal("Another PostgreSQL server is already called pg-main.", exception.Message);
    }

    [Theory]
    [InlineData("pg main", "10.0.0.5", 5432, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "", 5432, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "evil sslmode=disable", 5432, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "user@host", 5432, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "host:5432", 5432, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "10.0.0.5", 0, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "10.0.0.5", 65536, "production", "veyra_monitor", _password)]
    [InlineData("pg-main", "10.0.0.5", 5432, "", "veyra_monitor", _password)]
    [InlineData("pg-main", "10.0.0.5", 5432, "production", "", _password)]
    [InlineData("pg-main", "10.0.0.5", 5432, "production", "veyra monitor", _password)]
    [InlineData("pg-main", "10.0.0.5", 5432, "production", "veyra'monitor", _password)]
    [InlineData("pg-main", "10.0.0.5", 5432, "production", "veyra_monitor", "")]
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
            new PostgresServerInput(
                name,
                address,
                port,
                environment,
                null,
                login,
                password,
                PostgresEncryption.Required),
            _by,
            CancellationToken.None));
    }

    // The postgresql receiver writes the password into a key=value connection string without quoting it.
    [Theory]
    [InlineData("two words")]
    [InlineData("it's")]
    [InlineData(@"back\slash")]
    [InlineData("tab\there")]
    public async Task AddAsync_PasswordTheReceiverCannotPassOn_IsRejectedWithTheReason(string password)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var exception = await Assert.ThrowsAsync<AgentsValidationException>(() =>
            Service(database).AddAsync(Input() with { Password = password }, _by, CancellationToken.None));

        Assert.Equal("The password may not hold spaces, quotes, or backslashes.", exception.Message);
    }

    [Fact]
    public async Task AddAsync_UndefinedEncryption_IsRejected()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);

        var exception = await Assert.ThrowsAsync<AgentsValidationException>(() => Service(database).AddAsync(
            Input() with { Encryption = (PostgresEncryption)9 },
            _by,
            CancellationToken.None));

        Assert.Equal("Choose how the connection is encrypted.", exception.Message);
    }

    [Theory]
    [InlineData("pg-main.corp.local")]
    [InlineData("10.0.0.5")]
    [InlineData("[fd00::5]")]
    [InlineData("postgres")]
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
            Input() with { Address = "10.0.0.6", Port = 5433, Password = null, Encryption = PostgresEncryption.Off },
            _by,
            CancellationToken.None);

        Assert.NotNull(changed);
        Assert.Equal(added.CreatedAt, changed.CreatedAt);
        Assert.Equal(_clock.GetUtcNow(), changed.ChangedAt);
        var target = Assert.Single(await service.TargetsAsync(agent.Id, CancellationToken.None));
        Assert.Equal(_password, target.Password);
        Assert.Equal("10.0.0.6", target.Server.Address);
        Assert.Equal(5433, target.Server.Port);
        Assert.Equal(PostgresEncryption.Off, target.Server.Encryption);
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
    public async Task CountServersAsync_PostgresServer_CountsForItsAgent()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var agent = await SeedBuiltInAsync(database);

        await Service(database).AddAsync(Input(), _by, CancellationToken.None);

        Assert.Equal(1, await new AgentStore(database).CountServersAsync(agent.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ChangesAsync_AddChangeRemove_AreInTheSettingsHistoryWithFieldNamesOnly()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedBuiltInAsync(database);
        var service = Service(database);

        var added = await service.AddAsync(Input(), _by, CancellationToken.None);
        await service.UpdateAsync(added.Id, Input() with { Address = "10.0.0.6", Password = null }, _by, CancellationToken.None);
        await service.UpdateAsync(added.Id, Input() with { Address = "10.0.0.6", Password = null }, _by, CancellationToken.None);
        await service.RemoveAsync(added.Id, _by, CancellationToken.None);

        Assert.Equal(
            [
                (MonitoringSubject.PostgresServer, "pg-main", MonitoringChangeKind.Added, ""),
                (MonitoringSubject.PostgresServer, "pg-main", MonitoringChangeKind.Changed, "address"),
                (MonitoringSubject.PostgresServer, "pg-main", MonitoringChangeKind.Removed, ""),
            ],
            _history.Changes.Select(entry => (
                entry.Change.Subject,
                entry.Change.Name,
                entry.Change.Kind,
                string.Join(", ", entry.Change.Fields))));
        Assert.All(_history.Changes, entry => Assert.Same(_by, entry.By));
    }

    private static PostgresServerInput Input() =>
        new(
            Name: "pg-main",
            Address: "10.0.0.5",
            Port: 5432,
            Environment: "production",
            AgentId: null,
            Login: "veyra_monitor",
            Password: _password,
            Encryption: PostgresEncryption.TrustServerCertificate);

    private static async Task<Agent> SeedBuiltInAsync(NpgsqlDataSource database)
    {
        var agent = AgentsDatabase.BuiltIn();
        await new AgentStore(database).InsertAsync(agent, CancellationToken.None);

        return agent;
    }

    private PostgresServerService Service(NpgsqlDataSource database) =>
        new(
            new PostgresServerStore(database),
            new AgentStore(database),
            new PostgresPasswordProtector(_protection),
            _clock,
            _history,
            NullLogger<PostgresServerService>.Instance);

    private static async Task<bool> StoredTextContainsAsync(NpgsqlDataSource database, string text)
    {
        await using var command = database.CreateCommand(
            "SELECT count(*) FROM agents.postgres_server AS s WHERE s::text LIKE '%' || $1 || '%'");
        command.Parameters.AddWithValue(text);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }
}
