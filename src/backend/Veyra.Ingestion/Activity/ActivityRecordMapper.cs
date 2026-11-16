using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Activity;

/// <summary>
/// Recognizes the log records Veyra's agent sends from its own queries (ADR 0020) and turns them into database
/// activity. Every other record stays a log record.
/// </summary>
/// <remarks>
/// <para>
/// The agent's <c>sqlquery</c> receiver sends each row of a query as a log record, with the row's columns as
/// attributes named like the columns and one column as the body. A <c>veyra_record</c> column names the kind of row,
/// such as <c>sqlserver.session</c>, and a <c>sample</c> column holds the time of the read in Unix milliseconds, the
/// same for every row of one read, which is how the rows of one read are told apart from the next.
/// </para>
/// <para>
/// Columns arrive as text. A column that is missing or not a number reads as empty or 0, so a template from another
/// Veyra version never makes a whole batch fail.
/// </para>
/// </remarks>
internal static class ActivityRecordMapper
{
    /// <summary>The attribute that names the kind of row.</summary>
    public const string KindAttribute = "veyra_record";

    /// <summary>The kind of a row of the live view's sessions query.</summary>
    public const string SessionKind = "sqlserver.session";

    /// <summary>The kinds of row kept as records, by kind and key (ADR 0020).</summary>
    public static readonly IReadOnlySet<string> RecordKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "sqlserver.plan",
        "sqlserver.deadlock",
        "sqlserver.event",
        "sqlserver.snapshot",
        "sqlserver.querystore",
        "sqlserver.index",
        "sqlserver.job",
        "sqlserver.ag",
    };

    /// <summary>Whether a record is a row of one of Veyra's own queries.</summary>
    /// <param name="record">The record.</param>
    /// <returns><see langword="true"/> when it names a kind Veyra knows.</returns>
    public static bool IsActivity(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.Attributes.TryGetValue(KindAttribute, out var kind)
            && (kind == SessionKind || RecordKinds.Contains(kind));
    }

    /// <summary>Turns a row of one of Veyra's own queries into database activity.</summary>
    /// <param name="record">A record for which <see cref="IsActivity"/> is true.</param>
    /// <returns>The activity.</returns>
    public static DatabaseActivity Map(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var row = record.Attributes;

        if (row[KindAttribute] is var kind && kind != SessionKind)
        {
            return new ActivityRecord(
                record.ServiceName,
                record.Environment,
                Number(row, "sample") is var at and > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(at)
                    : record.Timestamp,
                kind,
                Text(row, "key"),
                row.Where(pair => pair.Key is not KindAttribute and not "sample" and not "key")
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                record.Body);
        }

        var sampledAt = Number(row, "sample") is var milliseconds and > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : record.Timestamp;
        var sessionId = (int)Number(row, "session_id");

        if (sessionId == 0)
        {
            return new ServerSample(
                record.ServiceName,
                record.Environment,
                sampledAt,
                TempdbSizeKilobytes: Number(row, "tempdb_size_kb"),
                TempdbFreeKilobytes: Number(row, "tempdb_free_kb"),
                GrantsWaiting: (int)Number(row, "grants_waiting"));
        }

        var transaction = Number(row, "transaction_ms");

        return new SessionSample(
            record.ServiceName,
            record.Environment,
            sampledAt,
            SessionId: sessionId,
            RequestStatus: Text(row, "request_status"),
            SessionStatus: Text(row, "session_status"),
            Command: Text(row, "command"),
            Database: Text(row, "database_name"),
            Program: Text(row, "program"),
            Host: Text(row, "host"),
            Login: Text(row, "login"),
            BlockedBy: (int)Number(row, "blocked_by"),
            WaitType: Text(row, "wait_type"),
            WaitMilliseconds: Number(row, "wait_ms"),
            WaitResource: Text(row, "wait_resource"),
            ElapsedMilliseconds: Number(row, "elapsed_ms"),
            CpuMilliseconds: Number(row, "cpu_ms"),
            Reads: Number(row, "reads"),
            Writes: Number(row, "writes"),
            TempdbKilobytes: Number(row, "tempdb_kb"),
            GrantKilobytes: Number(row, "grant_kb"),
            GrantRequestedKilobytes: Number(row, "grant_requested_kb"),
            GrantWaiting: Number(row, "grant_waiting") != 0,

            // The query writes -1 for a session without an open transaction, since a row may not hold NULL.
            TransactionMilliseconds: transaction < 0 ? null : transaction,
            TransactionLogRecords: Number(row, "transaction_log_records"),
            OpenTransactions: (int)Number(row, "open_transactions"),
            Isolation: (int)Number(row, "isolation"),
            PercentComplete: Fraction(row, "percent_complete"),
            RemainingMilliseconds: Number(row, "remaining_ms"),
            QueryHash: Text(row, "query_hash"),
            PlanHash: Text(row, "plan_hash"),
            IdleMilliseconds: Number(row, "idle_ms"),
            ConnectedSeconds: Number(row, "connected_seconds"),
            Text: record.Body,
            Running: Number(row, "running") != 0,
            Blocking: Number(row, "blocking") != 0);
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : "";

    private static long Number(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value)
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number)
            ? (long)number
            : 0;

    private static double Fraction(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value)
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number)
            ? number
            : 0;
}
