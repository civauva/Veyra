using Microsoft.Extensions.Time.Testing;
using Veyra.Access.History;
using Veyra.Access.Sessions;
using Veyra.Access.Tests.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Sessions;

[Collection(PostgresGroup.Name)]
public class SessionServiceTests(PostgresFixture postgres)
{
    private static readonly SignInClient _firefox = new(
        "198.51.100.7",
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64) Firefox/132.0");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task StartAsync_NewSession_IsActiveAndListedWithItsClient()
    {
        var (access, user) = await CreateAsync();

        var session = await access.Sessions.StartAsync(user.Id, SessionMethod.Code, _firefox, CancellationToken.None);

        var listed = Assert.Single(await access.Sessions.ListActiveAsync(user.Id, CancellationToken.None));
        Assert.Equal((session.Id, "198.51.100.7", SessionMethod.Code), (listed.Id, listed.Address, listed.Method));
        Assert.Equal(user.Id, (await access.Sessions.ValidateAsync(session.Id, CancellationToken.None))?.Id);
    }

    [Fact]
    public async Task ValidateAsync_EndedSession_IsRefused()
    {
        var (access, user) = await CreateAsync();
        var session = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);

        await access.Sessions.SignOutAsync(user.Id, session.Id, CancellationToken.None);

        Assert.Null(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_IdleLongerThanTheTimeout_IsRefused()
    {
        var (access, user) = await CreateAsync();
        var session = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));

        Assert.Null(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_SwitchedOffUser_IsRefused()
    {
        var (access, user) = await CreateAsync();
        var session = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);

        await access.Store.UpdateAsync(user.Id, null, true, null, CancellationToken.None);

        Assert.Null(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_RequestsWithinFiveMinutes_WriteLastActivityOnlyOnce()
    {
        var (access, user) = await CreateAsync();
        var session = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(3));
        await access.Sessions.ValidateAsync(session.Id, CancellationToken.None);
        var early = Assert.Single(await access.Sessions.ListActiveAsync(user.Id, CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(3));
        await access.Sessions.ValidateAsync(session.Id, CancellationToken.None);
        var later = Assert.Single(await access.Sessions.ListActiveAsync(user.Id, CancellationToken.None));

        Assert.Equal(session.StartedAt, early.LastActiveAt);
        Assert.Equal(_clock.GetUtcNow(), later.LastActiveAt);
    }

    [Fact]
    public async Task EndOthersAsync_KeepsTheCurrentSessionAndRecordsWhoEndedThem()
    {
        var (access, user) = await CreateAsync();
        var current = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);
        var phone = await access.Sessions.StartAsync(user.Id, SessionMethod.Password, _firefox, CancellationToken.None);
        var tablet = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);

        var ended = await access.Sessions.EndAllAsync(user.Id, current.Id, user.Id, CancellationToken.None);

        Assert.Equal(2, ended);
        Assert.NotNull(await access.Sessions.ValidateAsync(current.Id, CancellationToken.None));
        Assert.Null(await access.Sessions.ValidateAsync(phone.Id, CancellationToken.None));
        Assert.Null(await access.Sessions.ValidateAsync(tablet.Id, CancellationToken.None));
        var recorded = (await access.UserEvents.ListAsync(user.Id, CancellationToken.None))[0];
        Assert.Equal((UserEventKind.SessionsEnded, user.Id, "2"), (recorded.Kind, recorded.ActorId, recorded.Detail));
    }

    [Fact]
    public async Task EndAsync_SessionOfSomeoneElse_IsNotEnded()
    {
        var (access, user) = await CreateAsync();
        var session = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            _firefox,
            CancellationToken.None);

        var ended = await access.Sessions.EndAsync(Guid.NewGuid(), session.Id, user.Id, CancellationToken.None);

        Assert.False(ended);
        Assert.NotNull(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
    }

    private async Task<(AccessServices Access, Access.Users.AccessUser User)> CreateAsync()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var access = new AccessServices(database, _clock);
        var user = await access.Users.CreateAsync(
            "lena@example.com",
            "Lena Hoffmann",
            VeyraRole.Editor,
            "correct horse battery",
            null,
            null,
            CancellationToken.None);

        return (access, user);
    }
}
