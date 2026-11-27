using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using Veyra.Access.Settings;
using Veyra.Access.Users;
using Veyra.Core.Settings;
using Veyra.Testing;

namespace Veyra.Access.Tests.Settings;

[Collection(PostgresGroup.Name)]
public class MonitoringHistoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RecordAsync_SignedInPerson_IsNamedInTheHistory()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var store = new SettingStore(database, new EphemeralDataProtectionProvider(), new FakeTimeProvider());
        var history = new MonitoringHistory(store, new UserStore(database));
        var by = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, admin.ToString())]));

        await history.RecordAsync(
            new MonitoringChange(MonitoringSubject.PostgresServer, "pg-main", MonitoringChangeKind.Added, []),
            by,
            default);

        var change = Assert.Single((await store.HistoryAsync(new SettingHistoryQuery(), default)).Changes);
        Assert.Equal(
            ("pg-main", (Guid?)admin, "Admin", MonitoringSubject.PostgresServer),
            (change.Name, change.ChangedBy, change.ChangedByName, change.Subject));
    }

    [Fact]
    public async Task RecordAsync_VersionChange_KeepsTheVersionsBeforeAndAfter()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var store = new SettingStore(database, new EphemeralDataProtectionProvider(), new FakeTimeProvider());
        var history = new MonitoringHistory(store, new UserStore(database));

        await history.RecordAsync(
            new MonitoringChange(
                MonitoringSubject.SqlServer,
                "sql-prod-01",
                MonitoringChangeKind.Changed,
                ["version"],
                "0.1.0",
                "0.2.0"),
            Person(admin),
            default);

        var change = Assert.Single((await store.HistoryAsync(new SettingHistoryQuery(), default)).Changes);
        Assert.Equal(("0.1.0", "0.2.0"), (change.OldValue, change.NewValue));
    }

    [Fact]
    public async Task ListAsync_OneSystem_ListsItsChangesNewestFirstAndNoOthers()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
        var store = new SettingStore(database, new EphemeralDataProtectionProvider(), clock);
        var history = new MonitoringHistory(store, new UserStore(database));
        var by = Person(admin);

        await history.RecordAsync(
            new MonitoringChange(MonitoringSubject.SqlServer, "sql-prod-01", MonitoringChangeKind.Added, []),
            by,
            default);
        clock.Advance(TimeSpan.FromMinutes(1));
        await history.RecordAsync(
            new MonitoringChange(MonitoringSubject.PostgresServer, "sql-prod-01", MonitoringChangeKind.Added, []),
            by,
            default);
        await history.RecordAsync(
            new MonitoringChange(MonitoringSubject.SqlServer, "sql-test-01", MonitoringChangeKind.Added, []),
            by,
            default);
        await history.RecordAsync(
            new MonitoringChange(
                MonitoringSubject.SqlServer,
                "sql-prod-01",
                MonitoringChangeKind.Changed,
                ["version"],
                "0.2.0",
                "0.1.0"),
            by,
            default);

        var changes = await history.ListAsync(MonitoringSubject.SqlServer, "sql-prod-01", 10, default);

        Assert.Equal(
            [
                (clock.GetUtcNow(), (string?)"Admin", MonitoringChangeKind.Changed, (string?)"0.2.0", (string?)"0.1.0"),
                (clock.GetUtcNow().AddMinutes(-1), "Admin", MonitoringChangeKind.Added, null, null),
            ],
            changes.Select(change => (change.At, change.By, change.Kind, change.From, change.To)));
        Assert.Equal(["version"], changes[0].Fields);
    }

    [Fact]
    public async Task ListSavedKeysAsync_Prefix_ListsOnlySavedKeysUnderIt()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var store = new SettingStore(database, new EphemeralDataProtectionProvider(), new FakeTimeProvider());
        await store.SaveAsync(
            0,
            [
                new SettingWrite("Veyra:Alerting:DatabaseBlocking:WarningSessions", "9", Secret: false),
                new SettingWrite("Veyra:Storage:LogRetentionDays", "7", Secret: false),
            ],
            new SettingAuthor(admin, "Admin"),
            SettingChangeKind.Changed,
            null,
            default);

        var keys = await new SavedSettings(store).ListSavedKeysAsync("veyra:alerting:database", default);

        Assert.Equal(["Veyra:Alerting:DatabaseBlocking:WarningSessions"], keys);
    }

    private static ClaimsPrincipal Person(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())]));
}
