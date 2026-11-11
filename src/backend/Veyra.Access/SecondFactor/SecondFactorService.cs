using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Veyra.Access.History;
using Veyra.Access.Sessions;
using Veyra.Access.Settings;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.SecondFactor;

/// <summary>
/// Sets up, checks, and removes the authenticator-app second factor and its recovery codes (ADR 0011).
/// </summary>
/// <param name="store">Where second factors and recovery codes are stored.</param>
/// <param name="settings">Where the policy of who must use one is stored.</param>
/// <param name="events">Where changes are recorded.</param>
/// <param name="sessions">The sessions that changes end.</param>
/// <param name="protection">Encrypts the secrets, with the key ring of the session cookies.</param>
/// <param name="clock">The clock for codes and change times.</param>
/// <param name="logger">Receives the security events.</param>
internal sealed partial class SecondFactorService(
    SecondFactorStore store,
    AccessSettingStore settings,
    UserEventStore events,
    SessionStore sessions,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger<SecondFactorService> logger)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Veyra.Access.SecondFactor.v1");

    /// <summary>Finds a person's second factor.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The second factor, switched on or being set up, or <see langword="null"/>.</returns>
    public Task<SecondFactorState?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        store.FindAsync(userId, cancellationToken);

    /// <summary>Starts setting up: a new secret, shown once, that is not asked for until a code confirms it.</summary>
    /// <param name="user">The person.</param>
    /// <param name="cancellationToken">Cancels the setup.</param>
    /// <returns>The secret and its address, or <see langword="null"/> when a second factor is already on.</returns>
    public async Task<SecondFactorSetup?> StartSetupAsync(AccessUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var secret = Totp.NewSecret();
        var started = await store.StartSetupAsync(
            user.Id,
            _protector.Protect(Base32.Encode(secret)),
            clock.GetUtcNow(),
            cancellationToken);

        return started ? new SecondFactorSetup(Base32.Encode(secret), Totp.Uri(secret, user.Email)) : null;
    }

    /// <summary>
    /// Finishes setup with a working code: switches the second factor on, makes ten recovery codes, and signs the
    /// person out on every other browser.
    /// </summary>
    /// <param name="user">The person.</param>
    /// <param name="code">The code the app shows now.</param>
    /// <param name="currentSessionId">
    /// The session making the change, which stays; <see langword="null"/> ends all.
    /// </param>
    /// <param name="cancellationToken">Cancels the setup.</param>
    /// <returns>
    /// The recovery codes, shown once; <see langword="null"/> when the code is wrong or no setup waits.
    /// </returns>
    public async Task<IReadOnlyList<string>?> ConfirmSetupAsync(
        AccessUser user,
        string code,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var state = await store.FindAsync(user.Id, cancellationToken);

        if (state is null || state.Enabled)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var step = Totp.Verify(Secret(state), code, now, null);

        if (step is null)
        {
            return null;
        }

        var codes = RecoveryCodes.Generate();

        if (!await store.EnableAsync(
            user.Id,
            step.Value,
            [.. codes.Select(RecoveryCodes.Hash)],
            now,
            cancellationToken))
        {
            return null;
        }

        await sessions.EndAllAsync(user.Id, currentSessionId, now, cancellationToken);
        await events.RecordAsync(user.Id, now, UserEventKind.SecondFactorOn, user.Id, null, cancellationToken);
        LogSwitchedOn(user.Id);

        return codes;
    }

    /// <summary>
    /// Checks the second step of a sign-in: a code from the app, or one of the recovery codes.
    /// </summary>
    /// <param name="userId">The person signing in.</param>
    /// <param name="typed">What they typed.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>How they signed in, or <see langword="null"/> when the code is wrong or was used already.</returns>
    public async Task<SessionMethod?> VerifyAsync(Guid userId, string typed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(typed);

        var state = await store.FindAsync(userId, cancellationToken);

        if (state is not { Enabled: true })
        {
            return null;
        }

        var now = clock.GetUtcNow();

        if (RecoveryCodes.LooksLikeOne(typed))
        {
            if (!await store.UseRecoveryCodeAsync(userId, RecoveryCodes.Hash(typed), now, cancellationToken))
            {
                return null;
            }

            LogRecoveryCodeUsed(userId, state.RecoveryCodesLeft - 1);

            return SessionMethod.RecoveryCode;
        }

        var step = Totp.Verify(Secret(state), typed, now, state.LastUsedStep);

        // The store refuses a step that was used meanwhile, so two requests with one code cannot both pass.
        return step is not null && await store.UseStepAsync(userId, step.Value, cancellationToken)
            ? SessionMethod.Code
            : null;
    }

    /// <summary>
    /// Replaces all recovery codes with ten new ones and signs the person out on every other browser.
    /// </summary>
    /// <param name="user">The person.</param>
    /// <param name="currentSessionId">The session making the change, which stays.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The new codes, shown once.</returns>
    /// <exception cref="AccessConflictException">The person has no second factor switched on.</exception>
    public async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(
        AccessUser user,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (await store.FindAsync(user.Id, cancellationToken) is not { Enabled: true })
        {
            throw new AccessConflictException("Two-factor sign-in is not switched on.");
        }

        var codes = RecoveryCodes.Generate();
        var now = clock.GetUtcNow();

        await store.ReplaceRecoveryCodesAsync(user.Id, [.. codes.Select(RecoveryCodes.Hash)], cancellationToken);
        await sessions.EndAllAsync(user.Id, currentSessionId, now, cancellationToken);
        await events.RecordAsync(user.Id, now, UserEventKind.RecoveryCodesReplaced, user.Id, null, cancellationToken);

        return codes;
    }

    /// <summary>A person switches their own second factor off, which signs them out on every other browser.</summary>
    /// <param name="user">The person.</param>
    /// <param name="currentSessionId">The session making the change, which stays.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when a second factor was switched off.</returns>
    /// <exception cref="AccessConflictException">The policy requires a second factor of this person.</exception>
    public async Task<bool> SwitchOffAsync(
        AccessUser user,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (await IsRequiredAsync(user, cancellationToken))
        {
            throw new AccessConflictException("Your administrator requires two-factor sign-in for your account.");
        }

        if (!await store.RemoveAsync(user.Id, cancellationToken))
        {
            return false;
        }

        var now = clock.GetUtcNow();

        await sessions.EndAllAsync(user.Id, currentSessionId, now, cancellationToken);
        await events.RecordAsync(user.Id, now, UserEventKind.SecondFactorOff, user.Id, null, cancellationToken);
        LogSwitchedOff(user.Id);

        return true;
    }

    /// <summary>
    /// Removes someone's second factor for a lost phone, by an Admin or by the server's way back in. The person is
    /// signed out everywhere.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="actorId">The Admin, or <see langword="null"/> when Veyra itself removes it.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when a switched-on second factor was removed.</returns>
    public async Task<bool> RemoveAsync(Guid userId, Guid? actorId, CancellationToken cancellationToken)
    {
        if (!await store.RemoveAsync(userId, cancellationToken))
        {
            return false;
        }

        var now = clock.GetUtcNow();

        await sessions.EndAllAsync(userId, null, now, cancellationToken);
        await events.RecordAsync(userId, now, UserEventKind.SecondFactorRemoved, actorId, null, cancellationToken);
        LogRemoved(userId, actorId);

        return true;
    }

    /// <summary>
    /// The last way back in (ADR 0011): removes the second factor of the account with an email, at startup, for the
    /// only Admin who lost both phone and recovery codes. Only someone who can change the server's settings can use
    /// it, and that person already has full access.
    /// </summary>
    /// <param name="users">Where users are stored.</param>
    /// <param name="email">The email from <see cref="AccessOptions.RemoveSecondFactor"/>.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when a switched-on second factor was removed.</returns>
    public async Task<bool> RemoveForEmailAsync(UserStore users, string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (await users.FindByEmailAsync(email, cancellationToken) is not { } user
            || !await RemoveAsync(user.Id, null, cancellationToken))
        {
            LogNothingToRemove();

            return false;
        }

        LogRemovedAtStartup(user.Id);

        return true;
    }

    /// <summary>Whether the policy requires a second factor of a person.</summary>
    /// <param name="user">The person.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> when it does. People without a local password never need one here.</returns>
    public async Task<bool> IsRequiredAsync(AccessUser user, CancellationToken cancellationToken) =>
        (await settings.GetSecondFactorPolicyAsync(cancellationToken)).Requires(user);

    /// <summary>Reads who must use a second factor.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The policy.</returns>
    public Task<SecondFactorPolicy> GetPolicyAsync(CancellationToken cancellationToken) =>
        settings.GetSecondFactorPolicyAsync(cancellationToken);

    /// <summary>Sets who must use a second factor.</summary>
    /// <param name="policy">The policy.</param>
    /// <param name="actorId">The Admin who sets it.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>A task that completes when the policy is stored.</returns>
    public async Task SetPolicyAsync(SecondFactorPolicy policy, Guid actorId, CancellationToken cancellationToken)
    {
        await settings.SetSecondFactorPolicyAsync(policy, cancellationToken);
        LogPolicyChanged(policy, actorId);
    }

    private byte[] Secret(SecondFactorState state) => Base32.Decode(_protector.Unprotect(state.ProtectedSecret));

    [LoggerMessage(Level = LogLevel.Information, Message = "Security: user {UserId} switched two-factor sign-in on")]
    private partial void LogSwitchedOn(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} switched two-factor sign-in off")]
    private partial void LogSwitchedOff(Guid userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: second factor of user {UserId} removed by {ActorId}")]
    private partial void LogRemoved(Guid userId, Guid? actorId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: user {UserId} signed in with a recovery code, {Left} left")]
    private partial void LogRecoveryCodeUsed(Guid userId, int left);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: second factor now required for {Policy}, set by {ActorId}")]
    private partial void LogPolicyChanged(SecondFactorPolicy policy, Guid actorId);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Security: second factor of user {UserId} removed at startup by Veyra:Access:RemoveSecondFactor. "
            + "Take the setting out again.")]
    private partial void LogRemovedAtStartup(Guid userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Veyra:Access:RemoveSecondFactor is set, but that account has no second factor. "
            + "Take the setting out.")]
    private partial void LogNothingToRemove();
}
