using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;

namespace Veyra.Access.Users;

/// <summary>
/// Checks a local email and password, then a second factor when the account has one, with lockout after repeated
/// failures (ADR 0008, ADR 0011). Every attempt on an existing account is recorded with its address and browser.
/// </summary>
/// <remarks>
/// <para>
/// Every failure looks the same to the caller: wrong password, wrong code, unknown email, disabled user, and locked
/// account all return <see cref="SignInResult.Failed"/>. Telling them apart would show attackers which emails exist.
/// The real reason goes to the sign-in history and the security log instead.
/// </para>
/// <para>
/// An unknown email still costs one password hash, so response times do not reveal which emails exist either.
/// Attempts for unknown emails are not stored: they would keep strangers' typed-in addresses.
/// </para>
/// </remarks>
/// <param name="store">Where users are stored.</param>
/// <param name="signIns">Where attempts are recorded.</param>
/// <param name="secondFactor">Checks the second step and the policy.</param>
/// <param name="hasher">The PBKDF2 password hasher from ASP.NET Core Identity.</param>
/// <param name="options">The Access settings, for lockout and whether local sign-in is on.</param>
/// <param name="clock">The clock for lockouts and sign-in times.</param>
/// <param name="logger">Receives the security events of ADR 0008.</param>
internal sealed partial class SignInService(
    UserStore store,
    SignInEventStore signIns,
    SecondFactorService secondFactor,
    IPasswordHasher<AccessUser> hasher,
    IOptions<AccessOptions> options,
    TimeProvider clock,
    ILogger<SignInService> logger)
{
    // A real hash of a random password, verified when the email is unknown, so that path takes as long as a real one.
    private static readonly Lazy<string> _decoyHash = new(() =>
        new PasswordHasher<AccessUser>().HashPassword(null!, Guid.NewGuid().ToString("N")));

    /// <summary>
    /// The first step: checks the password. An account with a second factor, or one that must set one up, does not
    /// sign in yet.
    /// </summary>
    /// <param name="email">The email address as typed.</param>
    /// <param name="password">The password as typed.</param>
    /// <param name="client">Where the attempt comes from.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Signed in, the next step with the user, or <see cref="SignInResult.Failed"/>.</returns>
    public async Task<SignInResult> SignInAsync(
        string email,
        string password,
        SignInClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var settings = options.Value;

        if (!settings.LocalSignInEnabled)
        {
            LogRejected("local sign-in is turned off", null);

            return SignInResult.Failed;
        }

        var user = await store.FindByEmailAsync(email, cancellationToken);
        var now = clock.GetUtcNow();

        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(null!, _decoyHash.Value, password);
            LogRejected(user is null ? "unknown email" : "account has no local password", user?.Id);

            return SignInResult.Failed;
        }

        if (await RefuseAsync(user, now, client, cancellationToken))
        {
            return SignInResult.Failed;
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(user, now, SignInOutcome.WrongPassword, client, cancellationToken);

            return SignInResult.Failed;
        }

        // Failures are not cleared yet when a code follows: wrong codes add to the same count.
        if (await secondFactor.FindAsync(user.Id, cancellationToken) is { Enabled: true })
        {
            return new SignInResult(SignInStep.NeedsCode, user);
        }

        if (await secondFactor.IsRequiredAsync(user, cancellationToken))
        {
            return new SignInResult(SignInStep.NeedsSetup, user);
        }

        // The hash format was strengthened since this password was set, for example more iterations: store the new one.
        var newHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? hasher.HashPassword(user, password)
            : null;

        return await FinishAsync(user, SessionMethod.Password, client, newHash, cancellationToken);
    }

    /// <summary>The second step: checks a code from the app or a recovery code.</summary>
    /// <param name="userId">The person whose password was right.</param>
    /// <param name="typed">The code as typed.</param>
    /// <param name="client">Where the attempt comes from.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Signed in with the method used, or <see cref="SignInResult.Failed"/>.</returns>
    public async Task<SignInResult> SecondStepAsync(
        Guid userId,
        string typed,
        SignInClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var user = await store.FindByIdAsync(userId, cancellationToken);
        var now = clock.GetUtcNow();

        if (user is null || await RefuseAsync(user, now, client, cancellationToken))
        {
            return SignInResult.Failed;
        }

        if (await secondFactor.VerifyAsync(user.Id, typed, cancellationToken) is not { } method)
        {
            await RecordFailureAsync(user, now, SignInOutcome.WrongCode, client, cancellationToken);

            return SignInResult.Failed;
        }

        return await FinishAsync(user, method, client, null, cancellationToken);
    }

    /// <summary>
    /// Completes a sign-in whose second factor was just set up and confirmed, as the policy asked.
    /// </summary>
    /// <param name="user">The person.</param>
    /// <param name="client">Where the sign-in comes from.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Signed in with a code.</returns>
    public Task<SignInResult> FinishAfterSetupAsync(
        AccessUser user,
        SignInClient client,
        CancellationToken cancellationToken) =>
        FinishAsync(user, SessionMethod.Code, client, null, cancellationToken);

    private async Task<SignInResult> FinishAsync(
        AccessUser user,
        SessionMethod method,
        SignInClient client,
        string? newHash,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var outcome = method switch
        {
            SessionMethod.Code => SignInOutcome.SignedInWithCode,
            SessionMethod.RecoveryCode => SignInOutcome.SignedInWithRecoveryCode,
            _ => SignInOutcome.SignedIn,
        };

        await store.RecordSignInAsync(user.Id, now, newHash, cancellationToken);
        await signIns.RecordAsync(user.Id, now, outcome, client.Address, client.UserAgent, cancellationToken);
        LogSignedIn(user.Id);

        return new SignInResult(
            SignInStep.SignedIn,
            user with { FailedSignIns = 0, LockedUntil = null, LastSignInAt = now },
            method);
    }

    // A switched-off or locked account is refused before any password or code is checked.
    private async Task<bool> RefuseAsync(
        AccessUser user,
        DateTimeOffset now,
        SignInClient client,
        CancellationToken cancellationToken)
    {
        if (user.Disabled)
        {
            await signIns.RecordAsync(
                user.Id,
                now,
                SignInOutcome.SwitchedOff,
                client.Address,
                client.UserAgent,
                cancellationToken);
            LogRejected("account is disabled", user.Id);

            return true;
        }

        if (user.LockedUntil > now)
        {
            await signIns.RecordAsync(
                user.Id,
                now,
                SignInOutcome.Locked,
                client.Address,
                client.UserAgent,
                cancellationToken);
            LogRejected("account is locked", user.Id);

            return true;
        }

        return false;
    }

    private async Task RecordFailureAsync(
        AccessUser user,
        DateTimeOffset now,
        SignInOutcome outcome,
        SignInClient client,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var failures = user.FailedSignIns + 1;

        await signIns.RecordAsync(user.Id, now, outcome, client.Address, client.UserAgent, cancellationToken);

        if (failures >= settings.LockoutThreshold)
        {
            // The count starts again after the lockout, so the next lockout needs another full series of failures.
            await store.RecordFailedSignInAsync(user.Id, 0, now + settings.LockoutDuration, cancellationToken);
            LogLockedOut(user.Id, settings.LockoutDuration);

            return;
        }

        await store.RecordFailedSignInAsync(user.Id, failures, null, cancellationToken);
        LogRejected(outcome == SignInOutcome.WrongCode ? "wrong code" : "wrong password", user.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Security: user {UserId} signed in")]
    private partial void LogSignedIn(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: sign-in rejected, {Reason}, user {UserId}")]
    private partial void LogRejected(string reason, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} locked out for {Duration}")]
    private partial void LogLockedOut(Guid userId, TimeSpan duration);
}
