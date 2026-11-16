using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Agents;

[Collection(PostgresGroup.Name)]
public sealed class BuiltInAgentSetupTests(PostgresFixture postgres) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("veyra-agent-").FullName;

    private readonly FakeKeys _keys = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private string SupervisorFile => Path.Combine(_directory, BuiltInAgentSetup.FileName);

    [Fact]
    public async Task PrepareAsync_NoDirectory_SetsUpNoAgent()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        await Setup(database, directory: null).PrepareAsync(CancellationToken.None);

        Assert.Null(await new AgentStore(database).FindBuiltInAsync(CancellationToken.None));
        Assert.Empty(_keys.Issued);
    }

    [Fact]
    public async Task PrepareAsync_FirstStart_CreatesTheAgentAndWritesItsStartupFile()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        await Setup(database).PrepareAsync(CancellationToken.None);

        var agent = await new AgentStore(database).FindBuiltInAsync(CancellationToken.None);
        var key = Assert.Single(_keys.Issued);
        Assert.NotNull(agent);
        Assert.Equal(key.KeyId, agent.IngestionKeyId);
        Assert.Equal("Built in", agent.Name);

        var text = await File.ReadAllTextAsync(SupervisorFile);
        Assert.Contains("endpoint: http://veyra:8080/v1/opamp", text, StringComparison.Ordinal);
        Assert.Contains($"Authorization: Bearer {key.Secret}", text, StringComparison.Ordinal);
        Assert.Contains("reports_own_logs: true", text, StringComparison.Ordinal);
        Assert.Equal(key.Secret, BuiltInAgentSetup.ReadKey(text));
    }

    [Fact]
    public async Task PrepareAsync_LaterStart_KeepsTheAgentAndItsKey()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await Setup(database).PrepareAsync(CancellationToken.None);
        var first = await File.ReadAllTextAsync(SupervisorFile);

        await Setup(database).PrepareAsync(CancellationToken.None);

        Assert.Single(_keys.Issued);
        Assert.Equal(first, await File.ReadAllTextAsync(SupervisorFile));
    }

    [Fact]
    public async Task PrepareAsync_StartupFileLost_GivesTheSameAgentANewKeyAndRevokesTheOld()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await Setup(database).PrepareAsync(CancellationToken.None);
        var agent = await new AgentStore(database).FindBuiltInAsync(CancellationToken.None);
        File.Delete(SupervisorFile);

        await Setup(database).PrepareAsync(CancellationToken.None);

        var again = await new AgentStore(database).FindBuiltInAsync(CancellationToken.None);
        Assert.Equal(2, _keys.Issued.Count);
        Assert.Equal(agent!.Id, again!.Id);
        Assert.Equal(_keys.Issued[1].KeyId, again.IngestionKeyId);
        Assert.Equal([_keys.Issued[0].KeyId], _keys.Revoked);
        Assert.Contains(_keys.Issued[1].Secret, await File.ReadAllTextAsync(SupervisorFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_KeyRevokedOnTheKeysPage_IssuesANewOne()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await Setup(database).PrepareAsync(CancellationToken.None);
        await _keys.RevokeAsync(_keys.Issued[0].KeyId, CancellationToken.None);

        await Setup(database).PrepareAsync(CancellationToken.None);

        Assert.Equal(2, _keys.Issued.Count);
        Assert.Contains(_keys.Issued[1].Secret, await File.ReadAllTextAsync(SupervisorFile), StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BuiltInAgentSetup Setup(NpgsqlDataSource database, string? directory = "") =>
        new(
            new AgentStore(database),
            _keys,
            _keys,
            new TestOptionsMonitor<AgentsOptions>(new AgentsOptions
            {
                BuiltInDirectory = directory == "" ? _directory : directory,
            }),
            _clock,
            NullLogger<BuiltInAgentSetup>.Instance);
}
