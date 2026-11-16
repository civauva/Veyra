using Veyra.Core.Activity;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Live;

/// <summary>
/// The figures of a server's newest live read that catalog rules judge (ADR 0020, area 3). Every read gives every
/// figure, 0 when nothing is wrong, so alerts resolve. Pure logic, tested without a database.
/// </summary>
internal static class LiveMeasures
{
    /// <summary>The longest any request has waited for another session, in seconds.</summary>
    public const string HeadBlockerSeconds = "head-blocker-seconds";

    /// <summary>The oldest transaction a session holds open without running anything, in seconds.</summary>
    public const string OpenTransactionSeconds = "open-transaction-seconds";

    /// <summary>The largest share of tempdb one session holds, from 0 to 1.</summary>
    public const string TempdbSessionShare = "tempdb-session-share";

    /// <summary>Measures one read.</summary>
    /// <param name="snapshot">The server's newest read.</param>
    /// <returns>The figures.</returns>
    public static IEnumerable<DatabaseMeasure> Of(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var server = new TelemetryBlock(Core.Alerts.AlertBlockKind.Database, snapshot.Block);
        var sessions = snapshot.Sessions;
        var tempdb = snapshot.Server.TempdbSizeKilobytes;

        yield return new DatabaseMeasure(
            server,
            snapshot.Environment,
            HeadBlockerSeconds,
            null,
            sessions.Where(session => session.BlockedBy > 0)
                .Select(session => session.WaitMilliseconds / 1000d)
                .DefaultIfEmpty(0)
                .Max());

        yield return new DatabaseMeasure(
            server,
            snapshot.Environment,
            OpenTransactionSeconds,
            null,
            sessions.Where(session => !session.Running && session.TransactionMilliseconds is not null)
                .Select(session => session.TransactionMilliseconds!.Value / 1000d)
                .DefaultIfEmpty(0)
                .Max());

        yield return new DatabaseMeasure(
            server,
            snapshot.Environment,
            TempdbSessionShare,
            null,
            tempdb <= 0
                ? 0
                : sessions.Select(session => (double)session.TempdbKilobytes / tempdb).DefaultIfEmpty(0).Max());
    }
}
