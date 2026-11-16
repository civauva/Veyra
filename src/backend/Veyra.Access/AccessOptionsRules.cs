using Veyra.Core.Settings;

namespace Veyra.Access;

/// <summary>
/// What <see cref="AccessOptions"/> must meet, at startup and before the Settings screen saves a change.
/// </summary>
public sealed class AccessOptionsRules : OptionsRules<AccessOptions>
{
    /// <inheritdoc />
    protected override IEnumerable<(bool Holds, string Message)> Rules(AccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Single sign-on is not built yet (ADR 0008), so without local sign-in nobody could sign in at all.
        yield return (
            options.LocalSignInEnabled,
            "Veyra:Access:LocalSignInEnabled must stay on until single sign-on is set up, or nobody could sign in.");
        yield return (options.LockoutThreshold > 0, "Veyra:Access:LockoutThreshold must be greater than zero.");
        yield return (options.LockoutDuration >= TimeSpan.Zero, "Veyra:Access:LockoutDuration must not be negative.");
        yield return (options.MinimumPasswordLength >= 12, "Veyra:Access:MinimumPasswordLength must be at least 12.");
        yield return (options.SessionIdleTimeout > TimeSpan.Zero, "Veyra:Access:SessionIdleTimeout must be positive.");
        yield return (options.KeyCacheDuration >= TimeSpan.Zero, "Veyra:Access:KeyCacheDuration must not be negative.");
        yield return (options.SetupTokenLifetime > TimeSpan.Zero, "Veyra:Access:SetupTokenLifetime must be positive.");
        yield return (
            options.SignInHistoryDays is >= 1 and <= 3650,
            "Veyra:Access:SignInHistoryDays must be between 1 and 3650.");
    }
}
