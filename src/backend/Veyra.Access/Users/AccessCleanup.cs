using Microsoft.Extensions.Options;
using Veyra.Access.History;
using Veyra.Access.Sessions;
using Veyra.Access.Settings;

namespace Veyra.Access.Users;

/// <summary>
/// Deletes sign-in attempts and sessions older than <see cref="AccessOptions.SignInHistoryDays"/> (ADR 0011), which
/// hold addresses, personal data; and settings changes older than a year (ADR 0017).
/// </summary>
/// <param name="signIns">Where sign-in attempts are stored.</param>
/// <param name="sessions">Where sessions are stored.</param>
/// <param name="settings">Where settings and their history are stored.</param>
/// <param name="options">The Access settings, for how long history is kept.</param>
/// <param name="clock">The clock for what is old.</param>
internal sealed class AccessCleanup(
    SignInEventStore signIns,
    SessionStore sessions,
    SettingStore settings,
    IOptionsMonitor<AccessOptions> options,
    TimeProvider clock)
{
    // ADR 0017 keeps the settings history for a year; saved values stay.
    private static readonly TimeSpan _settingHistory = TimeSpan.FromDays(365);

    /// <summary>Deletes what is older than the kept history.</summary>
    /// <param name="cancellationToken">Cancels the deletes.</param>
    /// <returns>How many attempts and sessions were deleted together.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var before = now.AddDays(-options.CurrentValue.SignInHistoryDays);

        await settings.DeleteHistoryBeforeAsync(now - _settingHistory, cancellationToken);

        return await signIns.DeleteBeforeAsync(before, cancellationToken)
            + await sessions.DeleteBeforeAsync(before, cancellationToken);
    }
}
