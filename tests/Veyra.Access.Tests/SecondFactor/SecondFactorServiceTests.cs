using Microsoft.Extensions.Time.Testing;
using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Tests.Users;
using Veyra.Access.Users;
using Veyra.Core.Security;
using Veyra.Testing;

namespace Veyra.Access.Tests.SecondFactor;

[Collection(PostgresGroup.Name)]
public class SecondFactorServiceTests(PostgresFixture postgres)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ConfirmSetupAsync_RightCode_SwitchesItOnWithTenRecoveryCodes()
    {
        var (access, user) = await CreateAsync();
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;

        var codes = await access.SecondFactor.ConfirmSetupAsync(
            user,
            access.CodeFor(setup.Secret),
            null,
            CancellationToken.None);

        Assert.Equal(10, codes!.Count);
        var state = await access.SecondFactor.FindAsync(user.Id, CancellationToken.None);
        Assert.True(state!.Enabled);
        Assert.Equal(10, state.RecoveryCodesLeft);
        Assert.StartsWith(
            "otpauth://totp/Veyra:lena%40example.com?secret=" + setup.Secret,
            setup.Uri,
            StringComparison.Ordinal);
        Assert.Equal(
            UserEventKind.SecondFactorOn,
            (await access.UserEvents.ListAsync(user.Id, CancellationToken.None))[0].Kind);
    }

    [Fact]
    public async Task ConfirmSetupAsync_WrongCode_LeavesItOff()
    {
        var (access, user) = await CreateAsync();
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;
        var wrong = access.CodeFor(setup.Secret) == "000000" ? "111111" : "000000";

        var codes = await access.SecondFactor.ConfirmSetupAsync(user, wrong, null, CancellationToken.None);

        Assert.Null(codes);
        Assert.False((await access.SecondFactor.FindAsync(user.Id, CancellationToken.None))!.Enabled);
    }

    [Fact]
    public async Task StartSetupAsync_AlreadyOn_ReturnsNothing()
    {
        var (access, user) = await CreateAsync();
        await SwitchOnAsync(access, user);

        Assert.Null(await access.SecondFactor.StartSetupAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_SameCodeTwice_AcceptsOnlyTheFirst()
    {
        var (access, user) = await CreateAsync();
        var secret = await SwitchOnAsync(access, user);
        _clock.Advance(TimeSpan.FromSeconds(30));
        var code = access.CodeFor(secret);

        var first = await access.SecondFactor.VerifyAsync(user.Id, code, CancellationToken.None);
        var second = await access.SecondFactor.VerifyAsync(user.Id, code, CancellationToken.None);

        Assert.Equal(SessionMethod.Code, first);
        Assert.Null(second);
    }

    [Fact]
    public async Task VerifyAsync_RecoveryCode_WorksOnceAndCountsDown()
    {
        var (access, user) = await CreateAsync();
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;
        var codes = (await access.SecondFactor.ConfirmSetupAsync(
            user,
            access.CodeFor(setup.Secret),
            null,
            CancellationToken.None))!;

        var first = await access.SecondFactor.VerifyAsync(user.Id, codes[3].ToLowerInvariant(), CancellationToken.None);
        var again = await access.SecondFactor.VerifyAsync(user.Id, codes[3], CancellationToken.None);

        Assert.Equal(SessionMethod.RecoveryCode, first);
        Assert.Null(again);
        Assert.Equal(9, (await access.SecondFactor.FindAsync(user.Id, CancellationToken.None))!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task ReplaceRecoveryCodesAsync_OldCodesStopWorking()
    {
        var (access, user) = await CreateAsync();
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;
        var old = (await access.SecondFactor.ConfirmSetupAsync(
            user,
            access.CodeFor(setup.Secret),
            null,
            CancellationToken.None))!;

        var fresh = await access.SecondFactor.ReplaceRecoveryCodesAsync(user, null, CancellationToken.None);

        Assert.Null(await access.SecondFactor.VerifyAsync(user.Id, old[0], CancellationToken.None));
        Assert.Equal(
            SessionMethod.RecoveryCode,
            await access.SecondFactor.VerifyAsync(user.Id, fresh[0], CancellationToken.None));
    }

    [Fact]
    public async Task SwitchOnAndOff_EndOtherSessionsButNotTheCurrentOne()
    {
        var (access, user) = await CreateAsync();
        var current = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);
        var other = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Password,
            SignInClient.Unknown,
            CancellationToken.None);
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;

        await access.SecondFactor.ConfirmSetupAsync(
            user,
            access.CodeFor(setup.Secret),
            current.Id,
            CancellationToken.None);

        Assert.NotNull(await access.Sessions.ValidateAsync(current.Id, CancellationToken.None));
        Assert.Null(await access.Sessions.ValidateAsync(other.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SwitchOffAsync_RequiredByThePolicy_IsRefused()
    {
        var (access, user) = await CreateAsync(VeyraRole.Admin);
        await SwitchOnAsync(access, user);
        await access.SecondFactor.SetPolicyAsync(SecondFactorPolicy.Admins, user.Id, CancellationToken.None);

        await Assert.ThrowsAsync<AccessConflictException>(
            () => access.SecondFactor.SwitchOffAsync(user, null, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_ByAnAdmin_RemovesItEndsEverySessionAndRecordsWho()
    {
        var (access, user) = await CreateAsync();
        await SwitchOnAsync(access, user);
        var session = await access.Sessions.StartAsync(
            user.Id,
            SessionMethod.Code,
            SignInClient.Unknown,
            CancellationToken.None);
        var admin = (await access.Users.CreateAsync(
            "dana@example.com",
            "Dana Kim",
            VeyraRole.Admin,
            "another long secret",
            null,
            null,
            CancellationToken.None)).Id;

        var removed = await access.SecondFactor.RemoveAsync(user.Id, admin, CancellationToken.None);

        Assert.True(removed);
        Assert.Null(await access.SecondFactor.FindAsync(user.Id, CancellationToken.None));
        Assert.Null(await access.Sessions.ValidateAsync(session.Id, CancellationToken.None));
        var latest = (await access.UserEvents.ListAsync(user.Id, CancellationToken.None))[0];
        Assert.Equal((UserEventKind.SecondFactorRemoved, admin), (latest.Kind, latest.ActorId));
    }

    [Fact]
    public async Task RemoveForEmailAsync_ServerWayBackIn_RemovesItWithoutAnActor()
    {
        var (access, user) = await CreateAsync(VeyraRole.Admin);
        await SwitchOnAsync(access, user);

        var removed = await access.SecondFactor.RemoveForEmailAsync(
            access.Store,
            "LENA@example.com",
            CancellationToken.None);

        Assert.True(removed);
        Assert.Null(await access.SecondFactor.FindAsync(user.Id, CancellationToken.None));
        var latest = (await access.UserEvents.ListAsync(user.Id, CancellationToken.None))[0];
        Assert.Equal((UserEventKind.SecondFactorRemoved, (Guid?)null), (latest.Kind, latest.ActorId));
    }

    [Fact]
    public async Task RemoveForEmailAsync_UnknownEmailOrNoSecondFactor_DoesNothing()
    {
        var (access, _) = await CreateAsync();

        Assert.False(await access.SecondFactor.RemoveForEmailAsync(
            access.Store,
            "nobody@example.com",
            CancellationToken.None));
        Assert.False(await access.SecondFactor.RemoveForEmailAsync(
            access.Store,
            "lena@example.com",
            CancellationToken.None));
    }

    [Theory]
    [InlineData(SecondFactorPolicy.Nobody, VeyraRole.Admin, false)]
    [InlineData(SecondFactorPolicy.Admins, VeyraRole.Admin, true)]
    [InlineData(SecondFactorPolicy.Admins, VeyraRole.Editor, false)]
    [InlineData(SecondFactorPolicy.Everyone, VeyraRole.Viewer, true)]
    public async Task IsRequiredAsync_FollowsThePolicyAndTheRole(
        SecondFactorPolicy policy,
        VeyraRole role,
        bool expected)
    {
        var (access, user) = await CreateAsync(role);
        await access.Settings.SetSecondFactorPolicyAsync(policy, CancellationToken.None);

        Assert.Equal(expected, await access.SecondFactor.IsRequiredAsync(user, CancellationToken.None));
    }

    private static async Task<string> SwitchOnAsync(AccessServices access, AccessUser user)
    {
        var setup = (await access.SecondFactor.StartSetupAsync(user, CancellationToken.None))!;
        await access.SecondFactor.ConfirmSetupAsync(user, access.CodeFor(setup.Secret), null, CancellationToken.None);

        return setup.Secret;
    }

    private async Task<(AccessServices Access, AccessUser User)> CreateAsync(VeyraRole role = VeyraRole.Editor)
    {
        var database = await AccessDatabase.CreateAsync(postgres);
        var access = new AccessServices(database, _clock);
        var user = await access.Users.CreateAsync(
            "lena@example.com",
            "Lena Hoffmann",
            role,
            "correct horse battery",
            null,
            null,
            CancellationToken.None);

        return (access, user);
    }
}
