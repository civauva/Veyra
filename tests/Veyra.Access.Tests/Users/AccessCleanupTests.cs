using Microsoft.Extensions.Time.Testing;
using Veyra.Access.Sessions;
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
}
