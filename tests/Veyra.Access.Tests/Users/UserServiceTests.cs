using Microsoft.Extensions.Time.Testing;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Users;

[Collection(PostgresGroup.Name)]
public class UserServiceTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateAsync_ShortPassword_ThrowsValidationException()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);

        await Assert.ThrowsAsync<AccessValidationException>(() =>
            access.Users.CreateAsync(
                "ana@example.com",
                "Ana",
                VeyraRole.Viewer,
                "short",
                null,
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_InvalidEmail_ThrowsValidationException()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);

        await Assert.ThrowsAsync<AccessValidationException>(() =>
            access.Users.CreateAsync(
                "not-an-email",
                "Ana",
                VeyraRole.Viewer,
                _password,
                null,
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_SameEmailInOtherCase_ThrowsConflictException()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        await access.Users.CreateAsync(
            "ana@example.com",
            "Ana",
            VeyraRole.Viewer,
            _password,
            null,
            null,
            CancellationToken.None);

        await Assert.ThrowsAsync<AccessConflictException>(() =>
            access.Users.CreateAsync(
                "ANA@example.com",
                "Ana 2",
                VeyraRole.Viewer,
                _password,
                null,
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_DemotingTheLastAdmin_ThrowsConflictException()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var admin = await access.Users.CreateAsync(
            "root@example.com",
            "Root",
            VeyraRole.Admin,
            _password,
            null, null, CancellationToken.None);

        await Assert.ThrowsAsync<AccessConflictException>(() =>
            access.Users.UpdateAsync(
                admin.Id,
                VeyraRole.Editor,
                disabled: null,
                team: null,
                actorId: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_DisablingTheLastAdmin_ThrowsConflictException()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var admin = await access.Users.CreateAsync(
            "root@example.com",
            "Root",
            VeyraRole.Admin,
            _password,
            null, null, CancellationToken.None);

        await Assert.ThrowsAsync<AccessConflictException>(() =>
            access.Users.UpdateAsync(
                admin.Id,
                role: null,
                disabled: true,
                team: null,
                actorId: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_DemotingOneOfTwoAdmins_Succeeds()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        await access.Users.CreateAsync(
            "root@example.com",
            "Root",
            VeyraRole.Admin,
            _password,
            null, null, CancellationToken.None);
        var second = await access.Users.CreateAsync(
            "ops@example.com",
            "Ops",
            VeyraRole.Admin,
            _password,
            null, null, CancellationToken.None);

        var updated = await access.Users
            .UpdateAsync(
                second.Id,
                VeyraRole.Viewer,
                disabled: null,
                team: null,
                actorId: null,
                CancellationToken.None);

        Assert.Equal(VeyraRole.Viewer, updated!.Role);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ReturnsFalse()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var user = await access.Users.CreateAsync(
            "ana@example.com",
            "Ana",
            VeyraRole.Viewer,
            _password,
            null, null, CancellationToken.None);

        var changed = await access.Users
            .ChangePasswordAsync(user.Id, "wrong password!", "a brand new secret", null, CancellationToken.None);

        Assert.False(changed);
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_LetsTheNewPasswordSignIn()
    {
        var access = new AccessServices(await AccessDatabase.CreateAsync(postgres), _clock);
        var user = await access.Users.CreateAsync(
            "ana@example.com",
            "Ana",
            VeyraRole.Viewer,
            _password,
            null, null, CancellationToken.None);

        await access.Users.ChangePasswordAsync(user.Id, _password, "a brand new secret", null, CancellationToken.None);

        var withNew = await access.SignIn.SignInAsync(
            "ana@example.com",
            "a brand new secret",
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.True(withNew.Succeeded);
        Assert.False((await access.SignIn.SignInAsync(
            "ana@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None)).Succeeded);
    }
}
