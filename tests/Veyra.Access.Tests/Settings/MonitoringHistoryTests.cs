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
}
