using Microsoft.Extensions.Time.Testing;
using Veyra.Access.Sessions;
using Veyra.Access.Settings;
using Veyra.Access.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Users;

[Collection(PostgresGroup.Name)]
public class AccessCleanupTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RunAsync_DeletesSignInsAndSessionsOlderThanTheHistoryAndKeepsTheRest()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var user = await access.Users.CreateAsync(
            "ana@example.com",
            "Ana",
            VeyraRole.Viewer,
            _password,
            null,
            null,
            CancellationToken.None);
        await access.SignIn.SignInAsync("ana@example.com", _password, SignInClient.Unknown, CancellationToken.None);
        var old = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);
        await access.Sessions.SignOutAsync(user.Id, old.Id, CancellationToken.None);

        _clock.Advance(TimeSpan.FromDays(91));
        await access.SignIn.SignInAsync("ana@example.com", _password, SignInClient.Unknown, CancellationToken.None);
        await access.Sessions.StartAsync(user.Id, SessionMethod.Password, SignInClient.Unknown, CancellationToken.None);

        var deleted = await access.Cleanup.RunAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Single(await access.SignInEvents.ListAsync(user.Id, 10, CancellationToken.None));
        Assert.Single(await access.Sessions.ListActiveAsync(user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_DeletesSettingChangesOlderThanAYear_AndKeepsTheSavedValues()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var author = new SettingAuthor(null, "Lena Adler");
        var version = await access.SettingStore.SaveAsync(
            0,
            [new SettingWrite("Veyra:Storage:LogRetentionDays", "7", false)],
            author,
            SettingChangeKind.Changed,
            null,
            CancellationToken.None);

        _clock.Advance(TimeSpan.FromDays(366));
        await access.SettingStore.SaveAsync(
            version!.Value,
            [new SettingWrite("Veyra:Query:MaxPageSize", "500", false)],
            author,
            SettingChangeKind.Changed,
            null,
            CancellationToken.None);

        await access.Cleanup.RunAsync(CancellationToken.None);

        var history = await access.SettingStore.HistoryAsync(new SettingHistoryQuery(), CancellationToken.None);
        Assert.Equal(["Veyra:Query:MaxPageSize"], history.Changes.Select(change => change.Name));
        Assert.Equal(
            "7",
            (await access.SettingStore.ReadAllAsync(CancellationToken.None))["Veyra:Storage:LogRetentionDays"].Value);
    }
}
