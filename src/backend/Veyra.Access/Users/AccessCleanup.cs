using Microsoft.Extensions.Options;
using Veyra.Access.History;
using Veyra.Access.Sessions;

namespace Veyra.Access.Users;

/// <summary>
/// Deletes sign-in attempts and sessions older than <see cref="AccessOptions.SignInHistoryDays"/> (ADR 0011). They
/// hold addresses, which are personal data.
/// </summary>
/// <param name="signIns">Where sign-in attempts are stored.</param>
/// <param name="sessions">Where sessions are stored.</param>
/// <param name="options">The Access settings, for how long history is kept.</param>
/// <param name="clock">The clock for what is old.</param>
internal sealed class AccessCleanup(
    SignInEventStore signIns,
    SessionStore sessions,
    IOptions<AccessOptions> options,
    TimeProvider clock)
{
    /// <summary>Deletes what is older than the kept history.</summary>
    /// <param name="cancellationToken">Cancels the deletes.</param>
    /// <returns>How many attempts and sessions were deleted together.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow().AddDays(-options.Value.SignInHistoryDays);

        return await signIns.DeleteBeforeAsync(before, cancellationToken)
            + await sessions.DeleteBeforeAsync(before, cancellationToken);
    }
}
