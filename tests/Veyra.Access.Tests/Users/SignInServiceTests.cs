using Microsoft.Extensions.Time.Testing;
using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.Users;

[Collection(PostgresGroup.Name)]
public class SignInServiceTests(PostgresFixture postgres)
{
    private const string _password = "correct horse battery";

    private static readonly SignInClient _laptop = new(
        "198.51.100.7",
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64) Firefox/132.0");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SignInAsync_CorrectPassword_SucceedsAndRecordsTheTime()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync(
            "ana@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var user = await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None);
        Assert.Equal(_clock.GetUtcNow(), user!.LastSignInAt);
    }

    [Fact]
    public async Task SignInAsync_EmailInDifferentCase_Succeeds()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync(
            "ANA@Example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_WrongPassword_Fails()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync(
            "ana@example.com",
            "wrong password!",
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task SignInAsync_UnknownEmail_Fails()
    {
        var access = await CreateWithUserAsync();

        var result = await access.SignIn.SignInAsync(
            "nobody@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_FiveFailures_LocksTheAccountEvenForTheCorrectPassword()
    {
        var access = await CreateWithUserAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await access.SignIn.SignInAsync(
                "ana@example.com",
                "wrong password!",
                SignInClient.Unknown,
                CancellationToken.None);
        }

        var result = await access.SignIn.SignInAsync(
            "ana@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_AfterTheLockoutDuration_SucceedsAgain()
    {
        var access = await CreateWithUserAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await access.SignIn.SignInAsync(
                "ana@example.com",
                "wrong password!",
                SignInClient.Unknown,
                CancellationToken.None);
        }

        _clock.Advance(TimeSpan.FromMinutes(16));

        var result = await access.SignIn.SignInAsync(
            "ana@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_DisabledUser_Fails()
    {
        var access = await CreateWithUserAsync();
        var user = await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None);
        await access.Store.UpdateAsync(user!.Id, role: null, disabled: true, team: null, CancellationToken.None);

        var result = await access.SignIn.SignInAsync(
            "ana@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_LocalSignInTurnedOff_Fails()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var setup = new AccessServices(database, _clock);
        await setup.Users.CreateAsync(
            "ana@example.com",
            "Ana",
            VeyraRole.Viewer,
            _password,
            null,
            null,
            CancellationToken.None);
        var access = new AccessServices(database, _clock, new AccessOptions { LocalSignInEnabled = false });

        var result = await access.SignIn.SignInAsync(
            "ana@example.com",
            _password,
            SignInClient.Unknown,
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_WithSecondFactor_AsksForTheCodeBeforeSigningIn()
    {
        var access = await CreateWithUserAsync();
        await SwitchOnSecondFactorAsync(access);

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);

        Assert.Equal(SignInStep.NeedsCode, result.Step);
        Assert.False(result.Succeeded);
        Assert.Null((await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None))!.LastSignInAt);
        Assert.Empty(await access.SignInEvents.ListAsync(result.User!.Id, 10, CancellationToken.None));
    }

    [Fact]
    public async Task SecondStepAsync_RightCode_SignsInAndRecordsTheCode()
    {
        var access = await CreateWithUserAsync();
        var secret = await SwitchOnSecondFactorAsync(access);
        var first = await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var result = await access.SignIn.SecondStepAsync(
            first.User!.Id,
            access.CodeFor(secret),
            _laptop,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(SessionMethod.Code, result.Method);
        var recorded = Assert.Single(await access.SignInEvents.ListAsync(first.User.Id, 10, CancellationToken.None));
        Assert.Equal((SignInOutcome.SignedInWithCode, "198.51.100.7"), (recorded.Outcome, recorded.Address));
    }

    [Fact]
    public async Task SecondStepAsync_FiveWrongCodes_LockTheAccount()
    {
        var access = await CreateWithUserAsync();
        await SwitchOnSecondFactorAsync(access);
        var first = await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await access.SignIn.SecondStepAsync(first.User!.Id, "000000", _laptop, CancellationToken.None);
        }

        var again = await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);

        Assert.Equal(SignInStep.Failed, again.Step);
        var latest = (await access.SignInEvents.ListAsync(first.User!.Id, 10, CancellationToken.None))[0];
        Assert.Equal(SignInOutcome.Locked, latest.Outcome);
    }

    [Fact]
    public async Task SignInAsync_RequiredButNotSetUp_AsksForSetup()
    {
        var access = await CreateWithUserAsync();
        await access.Settings.SetSecondFactorPolicyAsync(SecondFactorPolicy.Everyone, CancellationToken.None);

        var result = await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);

        Assert.Equal(SignInStep.NeedsSetup, result.Step);
    }

    [Fact]
    public async Task SignInAsync_FromAnotherAddressAfterSigningIn_IsMarkedAsNew()
    {
        var access = await CreateWithUserAsync();
        var first = await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);

        await access.SignIn.SignInAsync(
            "ana@example.com",
            "wrong password!",
            new SignInClient("203.0.113.48", "Chrome on Windows"),
            CancellationToken.None);
        await access.SignIn.SignInAsync("ana@example.com", _password, _laptop, CancellationToken.None);

        var events = await access.SignInEvents.ListAsync(first.User!.Id, 10, CancellationToken.None);
        Assert.Equal(
            [(SignInOutcome.SignedIn, false), (SignInOutcome.WrongPassword, true), (SignInOutcome.SignedIn, false)],
            events.Reverse().Select(one => (one.Outcome, one.NewAddress)));
    }

    [Fact]
    public async Task SignInAsync_UnknownEmail_RecordsNoAttempt()
    {
        var access = await CreateWithUserAsync();
        var user = await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None);

        await access.SignIn.SignInAsync("nobody@example.com", _password, _laptop, CancellationToken.None);

        Assert.Empty(await access.SignInEvents.ListAsync(user!.Id, 10, CancellationToken.None));
    }


    private static async Task<string> SwitchOnSecondFactorAsync(AccessServices access)
    {
        var user = (await access.Store.FindByEmailAsync("ana@example.com", CancellationToken.None))!;
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;
        await access.SecondFactor.ConfirmSetupAsync(user, access.CodeFor(setup.Secret), null, CancellationToken.None);

        return setup.Secret;
    }

    private async Task<AccessServices> CreateWithUserAsync()
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var access = new AccessServices(database, _clock);

        await access.Users.CreateAsync(
            "ana@example.com",
            "Ana",
            VeyraRole.Viewer,
            _password,
            null,
            null,
            CancellationToken.None);

        return access;
    }
}
