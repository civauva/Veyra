using Microsoft.Extensions.Time.Testing;
using Veyra.Access.History;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Users;

/// <summary>Changes Admins and people make to accounts, and what each records and ends (ADR 0011).</summary>
[Collection(PostgresGroup.Name)]
public class UserChangeTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateAsync_ByAnAdmin_RecordsWhoAddedThePersonAndTheTeam()
    {
        var (access, admin) = await CreateAsync();

        var tom = await AddTomAsync(access, admin);

        Assert.Equal("Payments", (await access.Store.FindByIdAsync(tom.Id, CancellationToken.None))!.Team);
        var added = Assert.Single(await access.UserEvents.ListAsync(tom.Id, CancellationToken.None));
        Assert.Equal(
            (UserEventKind.Added, admin.Id, "Dana Kim", "Viewer"),
            (added.Kind, added.ActorId, added.ActorName, added.Detail));
    }

    [Fact]
    public async Task UpdateAsync_RoleAndTeam_RecordBothChanges()
    {
        var (access, admin) = await CreateAsync();
        var tom = await AddTomAsync(access, admin);

        await access.Users.UpdateAsync(tom.Id, VeyraRole.Editor, null, "Checkout", admin.Id, CancellationToken.None);

        var events = await access.UserEvents.ListAsync(tom.Id, CancellationToken.None);
        Assert.Contains(events, one => one is { Kind: UserEventKind.RoleChanged, Detail: "Viewer Editor" });
        Assert.Contains(events, one => one is { Kind: UserEventKind.TeamChanged, Detail: "Checkout" });
    }

    [Fact]
    public async Task UpdateAsync_SameRoleAndTeam_RecordsNothing()
    {
        var (access, admin) = await CreateAsync();
        var tom = await AddTomAsync(access, admin);

        await access.Users.UpdateAsync(tom.Id, VeyraRole.Viewer, null, "Payments", admin.Id, CancellationToken.None);

        Assert.Single(await access.UserEvents.ListAsync(tom.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_TeamLongerThan100Characters_Throws()
    {
        var (access, admin) = await CreateAsync();
        var tom = await AddTomAsync(access, admin);

        await Assert.ThrowsAsync<AccessValidationException>(() => access.Users.UpdateAsync(
            tom.Id,
            null,
            null,
            new string('x', 101),
            admin.Id,
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_SwitchOff_EndsEverySession()
    {
        var (access, admin) = await CreateAsync();
        var tom = await AddTomAsync(access, admin);
        var session = await access.Sessions.StartAsync(
            tom.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);

        await access.Users.UpdateAsync(tom.Id, null, true, null, admin.Id, CancellationToken.None);

        Assert.Empty(await access.Sessions.ListActiveAsync(tom.Id, CancellationToken.None));
        Assert.Null(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
        Assert.Equal(
            UserEventKind.SwitchedOff,
            (await access.UserEvents.ListAsync(tom.Id, CancellationToken.None))[0].Kind);
    }

    [Fact]
    public async Task ResetPasswordAsync_LiftsTheLockEndsSessionsAndRecordsWho()
    {
        var (access, admin) = await CreateAsync();
        var tom = await AddTomAsync(access, admin);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await access.SignIn.SignInAsync(
                "tom@example.com",
                "wrong password!",
                SignInClient.Unknown,
                CancellationToken.None);
        }

        var session = await access.Sessions.StartAsync(
            tom.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);

        await access.Users.ResetPasswordAsync(tom.Id, "copper tide amber sketch", admin.Id, CancellationToken.None);

        Assert.Null(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
        var signIn = await access.SignIn.SignInAsync(
            "tom@example.com",
            "copper tide amber sketch",
            SignInClient.Unknown,
            CancellationToken.None);
        Assert.True(signIn.Succeeded);
        var reset = (await access.UserEvents.ListAsync(tom.Id, CancellationToken.None))[0];
        Assert.Equal((UserEventKind.PasswordReset, admin.Id), (reset.Kind, reset.ActorId));
    }

    [Fact]
    public async Task ResetPasswordAsync_ShortPassword_Throws()
    {
        var (access, admin) = await CreateAsync();
        var tom = await AddTomAsync(access, admin);

        await Assert.ThrowsAsync<AccessValidationException>(
            () => access.Users.ResetPasswordAsync(tom.Id, "short", admin.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ChangePasswordAsync_EndsOtherSessionsButKeepsThisOne()
    {
        var (access, admin) = await CreateAsync();
        var current = await access.Sessions.StartAsync(
            admin.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);
        var other = await access.Sessions.StartAsync(
            admin.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);

        await access.Users.ChangePasswordAsync(
            admin.Id,
            _password,
            "a brand new secret",
            current.Id,
            CancellationToken.None);

        Assert.NotNull(await access.Sessions.ValidateAsync(current.Id, CancellationToken.None));
        Assert.Null(await access.Sessions.ValidateAsync(other.Id, CancellationToken.None));
        Assert.Equal(
            UserEventKind.PasswordChanged,
            (await access.UserEvents.ListAsync(admin.Id, CancellationToken.None))[0].Kind);
    }

    private static Task<AccessUser> AddTomAsync(AccessServices access, AccessUser admin) => access.Users.CreateAsync(
        "tom@example.com",
        "Tom Okafor",
        VeyraRole.Viewer,
        _password,
        "Payments",
        admin.Id,
        CancellationToken.None);

    private async Task<(AccessServices Access, AccessUser Admin)> CreateAsync()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var admin = await access.Users.CreateAsync(
            "dana@example.com",
            "Dana Kim",
            VeyraRole.Admin,
            _password,
            null,
            null,
            CancellationToken.None);

        return (access, admin);
    }
}
