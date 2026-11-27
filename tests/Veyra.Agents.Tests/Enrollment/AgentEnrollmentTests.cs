using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.Enrollment;
using Veyra.Core.Settings;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Enrollment;

[Collection(PostgresGroup.Name)]
public class AgentEnrollmentTests(PostgresFixture postgres)
{
    private static readonly Uri _veyra = new("https://veyra.example.com");

    private readonly FakeKeys _keys = new();

    private readonly FakeMonitoringHistory _history = new();

    private readonly ClaimsPrincipal _by = FakeMonitoringHistory.Admin;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AddAsync_Name_CreatesARemoteAgentWithoutAKeyAndACodeForAnHour()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        var added = await Enrollment(database).AddAsync("Company network", _by, CancellationToken.None);

        Assert.Equal("Company network", added.Agent.Name);
        Assert.Equal(AgentPlacement.Remote, added.Agent.Placement);
        Assert.Null(added.Agent.IngestionKeyId);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(60), added.ExpiresAt);
        Assert.NotNull(EnrollmentCode.Normalize(added.Code));
        Assert.Empty(_keys.Issued);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_NoName_IsRejected(string name)
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            Enrollment(database).AddAsync(name, _by, CancellationToken.None));
    }

    [Fact]
    public async Task EnrollAsync_Code_GivesTheAgentAKeyAndItsStartupFile()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);
        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);

        var enrolled = await enrollment.EnrollAsync(
            added.Code.ToLowerInvariant(),
            AgentPlatform.Windows,
            _veyra,
            CancellationToken.None);

        Assert.NotNull(enrolled);
        var key = Assert.Single(_keys.Issued);
        Assert.Equal("Company network", enrolled.AgentName);
        Assert.Contains($"Authorization: Bearer {key.Secret}", enrolled.SupervisorConfig, StringComparison.Ordinal);
        Assert.Contains(
            "endpoint: https://veyra.example.com/v1/opamp",
            enrolled.SupervisorConfig,
            StringComparison.Ordinal);
        Assert.Equal(
            key.KeyId,
            (await new AgentStore(database).FindAsync(added.Agent.Id, CancellationToken.None))?.IngestionKeyId);
    }

    [Fact]
    public async Task EnrollAsync_CodeUsedOnce_IsRefusedTheSecondTime()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);
        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);
        await enrollment.EnrollAsync(added.Code, AgentPlatform.Linux, _veyra, CancellationToken.None);

        var again = await enrollment.EnrollAsync(added.Code, AgentPlatform.Linux, _veyra, CancellationToken.None);

        Assert.Null(again);
        Assert.Single(_keys.Issued);
    }

    [Fact]
    public async Task EnrollAsync_CodeAfterAnHour_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);
        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(await enrollment.EnrollAsync(added.Code, AgentPlatform.Linux, _veyra, CancellationToken.None));
    }

    [Fact]
    public async Task EnrollAsync_UnknownCode_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        Assert.Null(await Enrollment(database).EnrollAsync(
            EnrollmentCode.Generate(),
            AgentPlatform.Linux,
            _veyra,
            CancellationToken.None));
    }

    [Fact]
    public async Task NewCodeAsync_AgentInstalledAgain_GetsANewKeyAndTheOldIsRevoked()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);
        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);
        await enrollment.EnrollAsync(added.Code, AgentPlatform.Windows, _veyra, CancellationToken.None);

        var code = await enrollment.NewCodeAsync(added.Agent.Id, _by, CancellationToken.None);
        await enrollment.EnrollAsync(code!.Code, AgentPlatform.Windows, _veyra, CancellationToken.None);

        Assert.Equal(2, _keys.Issued.Count);
        Assert.Equal([_keys.Issued[0].KeyId], _keys.Revoked);
    }

    [Fact]
    public async Task NewCodeAsync_BuiltInAgent_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var builtIn = AgentsDatabase.BuiltIn();
        await new AgentStore(database).InsertAsync(builtIn, CancellationToken.None);

        Assert.Null(await Enrollment(database).NewCodeAsync(builtIn.Id, _by, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_EnrolledAgent_RevokesItsKeyAndItsCodes()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);
        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);
        await enrollment.EnrollAsync(added.Code, AgentPlatform.Linux, _veyra, CancellationToken.None);
        var code = await enrollment.NewCodeAsync(added.Agent.Id, _by, CancellationToken.None);

        Assert.True(await enrollment.RemoveAsync(added.Agent.Id, _by, CancellationToken.None));

        Assert.Equal([_keys.Issued[0].KeyId], _keys.Revoked);
        Assert.Null(await new AgentStore(database).FindAsync(added.Agent.Id, CancellationToken.None));
        Assert.Null(await enrollment.EnrollAsync(code!.Code, AgentPlatform.Linux, _veyra, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_AgentThatStillReadsServers_IsAConflict()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);
        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);
        await using (var command = database.CreateCommand(
            """
            INSERT INTO agents.sql_server
                (id, name, address, port, environment, agent_id, login, password, encryption, created_at, changed_at,
                 template_version, template_set_at)
            VALUES (gen_random_uuid(), 'sql-prod-01', '10.0.0.5', 1433, 'production', $1, 'x', 'x', 1, now(), now(),
                    '0.2.0', now())
            """))
        {
            command.Parameters.AddWithValue(added.Agent.Id);
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<AgentsConflictException>(() =>
            enrollment.RemoveAsync(added.Agent.Id, _by, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_BuiltInAgent_IsRefused()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var builtIn = AgentsDatabase.BuiltIn();
        await new AgentStore(database).InsertAsync(builtIn, CancellationToken.None);

        await Assert.ThrowsAsync<AgentsValidationException>(() =>
            Enrollment(database).RemoveAsync(builtIn.Id, _by, CancellationToken.None));
    }

    [Fact]
    public async Task ChangesAsync_AddNewCodeRemove_AreInTheSettingsHistoryByWhoMadeThem()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        var enrollment = Enrollment(database);

        var added = await enrollment.AddAsync("Company network", _by, CancellationToken.None);
        await enrollment.NewCodeAsync(added.Agent.Id, _by, CancellationToken.None);
        await enrollment.RemoveAsync(added.Agent.Id, _by, CancellationToken.None);

        Assert.Equal(
            [
                (MonitoringChangeKind.Added, ""),
                (MonitoringChangeKind.Changed, "install code"),
                (MonitoringChangeKind.Removed, ""),
            ],
            _history.Changes.Select(entry => (entry.Change.Kind, string.Join(", ", entry.Change.Fields))));
        Assert.All(
            _history.Changes,
            entry => Assert.Equal((MonitoringSubject.Agent, "Company network"), (entry.Change.Subject, entry.Change.Name)));
        Assert.All(_history.Changes, entry => Assert.Same(_by, entry.By));
    }

    private AgentEnrollment Enrollment(NpgsqlDataSource database) =>
        new(
            new AgentStore(database),
            new EnrollmentStore(database),
            _keys,
            _clock,
            _history,
            NullLogger<AgentEnrollment>.Instance);
}
