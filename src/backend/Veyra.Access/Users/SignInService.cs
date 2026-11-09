using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veyra.Access.Users;

/// <summary>
/// Checks a local email and password, with lockout after repeated failures (ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// Every failure looks the same to the caller: wrong password, unknown email, disabled user, and locked account all
/// return <see cref="SignInResult.Failed"/>. Telling them apart would show attackers which emails exist. The real
/// reason goes to the security log instead.
/// </para>
/// <para>
/// An unknown email still costs one password hash, so response times do not reveal which emails exist either.
/// </para>
/// </remarks>
/// <param name="store">Where users are stored.</param>
/// <param name="hasher">The PBKDF2 password hasher from ASP.NET Core Identity.</param>
/// <param name="options">The Access settings, for lockout and whether local sign-in is on.</param>
/// <param name="clock">The clock for lockouts and sign-in times.</param>
/// <param name="logger">Receives the security events of ADR 0008.</param>
internal sealed partial class SignInService(
    UserStore store,
    IPasswordHasher<AccessUser> hasher,
    IOptions<AccessOptions> options,
    TimeProvider clock,
    ILogger<SignInService> logger)
{
    // A real hash of a random password, verified when the email is unknown, so that path takes as long as a real one.
    private static readonly Lazy<string> _decoyHash = new(() =>
        new PasswordHasher<AccessUser>().HashPassword(null!, Guid.NewGuid().ToString("N")));

    /// <summary>
    /// Checks the credentials and records the outcome.
    /// </summary>
    /// <param name="email">The email address as typed.</param>
    /// <param name="password">The password as typed.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The user when the sign-in succeeded; otherwise <see cref="SignInResult.Failed"/>.</returns>
    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
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

        if (user.Disabled)
        {
            LogRejected("account is disabled", user.Id);

            return SignInResult.Failed;
        }

        if (user.LockedUntil > now)
        {
            LogRejected("account is locked", user.Id);

            return SignInResult.Failed;
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(user, now, settings, cancellationToken);

            return SignInResult.Failed;
        }

        // The hash format was strengthened since this password was set, for example more iterations: store the new one.
        var newHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? hasher.HashPassword(user, password)
            : null;

        await store.RecordSignInAsync(user.Id, now, newHash, cancellationToken);
        LogSignedIn(user.Id);

        return new SignInResult(true, user with { FailedSignIns = 0, LockedUntil = null, LastSignInAt = now });
    }

    private async Task RecordFailureAsync(
        AccessUser user,
        DateTimeOffset now,
        AccessOptions settings,
        CancellationToken cancellationToken)
    {
        var failures = user.FailedSignIns + 1;

        if (failures >= settings.LockoutThreshold)
        {
            // The count starts again after the lockout, so the next lockout needs another full series of failures.
            await store.RecordFailedSignInAsync(user.Id, 0, now + settings.LockoutDuration, cancellationToken);
            LogLockedOut(user.Id, settings.LockoutDuration);

            return;
        }

        await store.RecordFailedSignInAsync(user.Id, failures, null, cancellationToken);
        LogRejected("wrong password", user.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Security: user {UserId} signed in")]
    private partial void LogSignedIn(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: sign-in rejected, {Reason}, user {UserId}")]
    private partial void LogRejected(string reason, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} locked out for {Duration}")]
    private partial void LogLockedOut(Guid userId, TimeSpan duration);
}
