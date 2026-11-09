using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Users;

[Collection(PostgresGroup.Name)]
public class SignInServiceTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SignInAsync_CorrectPassword_SucceedsAndRecordsTheTime()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, CancellationToken.None);

        Assert.True(result.Succeeded);
        var user = await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None);
        Assert.Equal(_clock.GetUtcNow(), user!.LastSignInAt);
    }

    [Fact]
    public async Task SignInAsync_EmailInDifferentCase_Succeeds()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync("ANA@Example.com", _password, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_WrongPassword_Fails()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync("ana@example.com", "wrong password!", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task SignInAsync_UnknownEmail_Fails()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync("nobody@example.com", _password, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_FiveFailures_LocksTheAccountEvenForTheCorrectPassword()
    {
        var access = await CreateWithUserAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await access.SignIn.SignInAsync("ana@example.com", "wrong password!", CancellationToken.None);
        }

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_AfterTheLockoutDuration_SucceedsAgain()
    {
        var access = await CreateWithUserAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await access.SignIn.SignInAsync("ana@example.com", "wrong password!", CancellationToken.None);
        }

        _clock.Advance(TimeSpan.FromMinutes(16));

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_DisabledUser_Fails()
    {
        var access = await CreateWithUserAsync();
        var user = await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None);
        await access.Store.UpdateAsync(user!.Id, role: null, disabled: true, CancellationToken.None);

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_LocalSignInTurnedOff_Fails()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var setup = new AccessServices(database, _clock);
        await setup.Users.CreateAsync("ana@example.com", "Ana", VeyraRole.Viewer, _password, CancellationToken.None);
        var access = new AccessServices(database, _clock, new AccessOptions { LocalSignInEnabled = false });

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    private async Task<AccessServices> CreateWithUserAsync()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var access = new AccessServices(database, _clock);

        await access.Users.CreateAsync("ana@example.com", "Ana", VeyraRole.Viewer, _password, CancellationToken.None);

        return access;
    }
}
