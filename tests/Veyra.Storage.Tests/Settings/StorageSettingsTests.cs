using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Core.Settings;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Storage.Settings;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Settings;

[Collection(PostgresGroup.Name)]
public class StorageSettingsTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReviewAsync_ShorterLogRetention_SaysWhatItDeletes_AndIsRisky()
    {
        await using var database = await CreateWithLogDaysAsync(-10, -5, 0);

        var note = Assert.Single(await Settings(database).ReviewAsync(
            [new SettingChange("Veyra:Storage:LogRetentionDays", "14", "3")],
            default));

        Assert.True(note.Risky);
        Assert.Equal("Veyra:Storage:LogRetentionDays", note.Key);
        Assert.Matches(@"^Deletes \d+ days of logs, [\d.]+ [kMG]?B, at the next hourly clean-up\.", note.Text);
        Assert.Contains("cannot be brought back", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_LongerRetention_SaysNothing()
    {
        await using var database = await CreateWithLogDaysAsync(-10, 0);

        var notes = await Settings(database).ReviewAsync(
            [new SettingChange("Veyra:Storage:LogRetentionDays", "14", "30")],
            default);

        Assert.Empty(notes);
    }

    [Fact]
    public async Task ReviewAsync_ShorterRetentionWithNoOldData_SaysNothingIsDeleted()
    {
        await using var database = await CreateWithLogDaysAsync(0);

        var note = Assert.Single(await Settings(database).ReviewAsync(
            [new SettingChange("Veyra:Storage:LogRetentionDays", "14", "7")],
            default));

        Assert.False(note.Risky);
        Assert.StartsWith("No stored logs are older than 7 days", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_NewSizeBudget_SaysHowMuchIsUsedNow()
    {
        await using var database = await CreateWithLogDaysAsync(-1, 0);

        var note = Assert.Single(await Settings(database).ReviewAsync(
            [new SettingChange("Veyra:Storage:MaxTraceStorageGigabytes", null, "5")],
            default));

        Assert.False(note.Risky);
        Assert.StartsWith("Traces use ", note.Text, StringComparison.Ordinal);
        Assert.Contains("the oldest days go first; today is always kept", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_BudgetBelowWhatIsUsed_IsRisky()
    {
        await using var database = await CreateWithLogDaysAsync(-1, 0);

        var note = Assert.Single(await Settings(database).ReviewAsync(
            [new SettingChange("Veyra:Storage:MaxLogStorageGigabytes", null, "0.000001")],
            default));

        Assert.True(note.Risky);
        Assert.Contains("deleted at the next hourly clean-up", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RetentionOfZeroDays_ReturnsAProblemForThatKey()
    {
        var candidate = new ConfigurationBuilder()
            .AddInMemoryCollection([KeyValuePair.Create("Veyra:Storage:LogRetentionDays", (string?)"0")])
            .Build();

        var problem = Assert.Single(Settings(null).Validate(candidate));

        Assert.Equal("Veyra:Storage:LogRetentionDays", problem.Key);
    }

    [Fact]
    public async Task ReviewAsync_StorageNotConfigured_StillWarnsThatDataGoes()
    {
        var note = Assert.Single(await Settings(null).ReviewAsync(
            [new SettingChange("Veyra:Storage:TraceRetentionDays", "7", "2")],
            default));

        Assert.True(note.Risky);
        Assert.Contains("cannot be brought back", note.Text, StringComparison.Ordinal);
    }

    private static StorageSettings Settings(NpgsqlDataSource? database) => new(
        database is null ? null : new StorageFootprint(database, new FakeTimeProvider(_now)),
        [new StorageOptionsRules()]);

    // A migrated database with log partitions, and a record, on each of the given days relative to now.
    private async Task<NpgsqlDataSource> CreateWithLogDaysAsync(params int[] days)
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new LogPartitions(database, TimeProvider.System, NullLogger<LogPartitions>.Instance);

        foreach (var day in days)
        {
            await partitions.EnsureAsync(_now.AddDays(day), default);
        }

        await new LogStore(database).WriteAsync(days.Select(day => Record(_now.AddDays(day))).ToList(), default);

        return database;
    }

    private static LogRecord Record(DateTimeOffset timestamp) => new(
        timestamp,
        timestamp,
        LogSeverity.Information,
        null,
        "stored",
        "payments",
        null,
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>());
}
