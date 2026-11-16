using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Access.Settings;
using Veyra.Testing;

namespace Veyra.Access.Tests.Settings;

[Collection(PostgresGroup.Name)]
public class SettingStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private const string _days = "Veyra:Storage:LogRetentionDays";

    private const string _password = "Veyra:Alerting:Notifications:Smtp:Password";

    [Fact]
    public async Task SaveAsync_NewValues_AreReadBackWithWhoAndWhen_AndRecordedInTheHistory()
    {
        var (store, admin) = await CreateAsync();

        var saved = await store.SaveAsync(
            0,
            [new SettingWrite(_days, "7", Secret: false)],
            new SettingAuthor(admin, "Lena Adler"),
            SettingChangeKind.Changed,
            null,
            default);

        var value = (await store.ReadAllAsync(default))[_days];
        Assert.Equal(("7", "Lena Adler", _now), (value.Value, value.ChangedByName, value.ChangedAt));
        var change = Assert.Single((await store.HistoryAsync(new SettingHistoryQuery(), default)).Changes);
        Assert.Equal((_days, (string?)null, (string?)"7", SettingChangeKind.Changed), (change.Name, change.OldValue, change.NewValue, change.Kind));
        Assert.Equal(change.Id, saved);
    }

    [Fact]
    public async Task SaveAsync_FromAnOlderVersion_IsRefused_AndChangesNothing()
    {
        var (store, admin) = await CreateAsync();
        var author = new SettingAuthor(admin, "Lena Adler");
        await store.SaveAsync(0, [new SettingWrite(_days, "7", false)], author, SettingChangeKind.Changed, null, default);

        var saved = await store.SaveAsync(
            0,
            [new SettingWrite(_days, "3", false)],
            author,
            SettingChangeKind.Changed,
            null,
            default);

        Assert.Null(saved);
        Assert.Equal("7", (await store.ReadAllAsync(default))[_days].Value);
    }

    [Fact]
    public async Task SaveAsync_Reset_RemovesTheValue_AndRecordsWhatItWas()
    {
        var (store, admin) = await CreateAsync();
        var author = new SettingAuthor(admin, "Lena Adler");
        var version = await store.SaveAsync(0, [new SettingWrite(_days, "7", false)], author, SettingChangeKind.Changed, null, default);

        await store.SaveAsync(version!.Value, [new SettingWrite(_days, null, false)], author, SettingChangeKind.Reset, null, default);

        Assert.DoesNotContain(_days, (await store.ReadAllAsync(default)).Keys);
        var reset = (await store.HistoryAsync(new SettingHistoryQuery(), default)).Changes[0];
        Assert.Equal(("7", (string?)null, SettingChangeKind.Reset), (reset.OldValue, reset.NewValue, reset.Kind));
    }

    [Fact]
    public async Task SaveAsync_Secret_IsStoredEncrypted_AndNeverInTheHistory()
    {
        var (store, admin) = await CreateAsync();

        await store.SaveAsync(
            0,
            [new SettingWrite(_password, "hunter2-but-longer", Secret: true)],
            new SettingAuthor(admin, "Lena Adler"),
            SettingChangeKind.Changed,
            null,
            default);

        var raw = await ScalarAsync(store.DataSource, $"SELECT value FROM access.setting WHERE name = '{_password}'");
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.Equal("hunter2-but-longer", (await store.ReadAllAsync(default))[_password].Value);
        var change = Assert.Single((await store.HistoryAsync(new SettingHistoryQuery(), default)).Changes);
        Assert.Equal((true, (string?)null, (string?)null), (change.Secret, change.OldValue, change.NewValue));
    }

    [Fact]
    public async Task ReadAllAsync_LeavesOutValuesThatAreNotSettings()
    {
        var (store, _) = await CreateAsync();
        await ScalarAsync(
            store.DataSource,
            "INSERT INTO access.setting (name, value) VALUES ('second_factor_required_for', 'Admins') RETURNING name");

        Assert.Empty(await store.ReadAllAsync(default));
    }

    [Fact]
    public async Task HistoryAsync_PagesNewestFirst_AndFiltersByName()
    {
        var (store, admin) = await CreateAsync();
        var author = new SettingAuthor(admin, "Lena Adler");
        long version = 0;

        foreach (var days in new[] { "7", "8", "9" })
        {
            version = (await store.SaveAsync(version, [new SettingWrite(_days, days, false), new SettingWrite("Veyra:Query:MaxPageSize", days + "00", false)], author, SettingChangeKind.Changed, null, default))!.Value;
        }

        var first = await store.HistoryAsync(new SettingHistoryQuery(Prefix: "Veyra:Storage:", Limit: 2), default);
        var second = await store.HistoryAsync(new SettingHistoryQuery(Prefix: "Veyra:Storage:", Limit: 2, Before: first.Next), default);

        Assert.Equal(["9", "8"], first.Changes.Select(change => change.NewValue));
        Assert.Equal(["7"], second.Changes.Select(change => change.NewValue));
        Assert.Null(second.Next);
    }

    private async Task<(SettingStore Store, Guid Admin)> CreateAsync()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);

        return (new SettingStore(database, new EphemeralDataProtectionProvider(), new FakeTimeProvider(_now)), admin);
    }

    private static async Task<string> ScalarAsync(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
