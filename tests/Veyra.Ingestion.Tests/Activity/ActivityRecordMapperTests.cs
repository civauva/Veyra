using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Ingestion.Activity;

namespace Veyra.Ingestion.Tests.Activity;

/// <summary>
/// The agent sends the rows of its own queries as log records (ADR 0020). These tests prove the rows of the live view's
/// sessions query become sessions and server figures with every column, and that other records stay logs.
/// </summary>
public class ActivityRecordMapperTests
{
    private static readonly DateTimeOffset _sample = new(2026, 11, 15, 12, 0, 0, 123, TimeSpan.Zero);

    [Fact]
    public void Map_SessionRow_KeepsEveryColumnAndTheReadsTime()
    {
        var row = Row(new Dictionary<string, string>
        {
            ["session_id"] = "117",
            ["request_status"] = "suspended",
            ["session_status"] = "running",
            ["command"] = "UPDATE",
            ["database_name"] = "shop",
            ["program"] = "app_shop",
            ["host"] = "web-22",
            ["login"] = "svc_shop",
            ["blocked_by"] = "88",
            ["wait_type"] = "LCK_M_U",
            ["wait_ms"] = "134000",
            ["wait_resource"] = "KEY: 5:72057594045726720 (8194443284a0)",
            ["elapsed_ms"] = "134200",
            ["cpu_ms"] = "12",
            ["reads"] = "94",
            ["writes"] = "5",
            ["tempdb_kb"] = "64",
            ["grant_kb"] = "1024",
            ["grant_requested_kb"] = "2048",
            ["grant_waiting"] = "1",
            ["transaction_ms"] = "4207",
            ["transaction_log_records"] = "2",
            ["open_transactions"] = "1",
            ["isolation"] = "2",
            ["percent_complete"] = "12.5",
            ["remaining_ms"] = "60000",
            ["query_hash"] = "0x6ACBDEE9D5014909",
            ["plan_hash"] = "0x88EB92735AB6DB89",
            ["idle_ms"] = "4207",
            ["connected_seconds"] = "7440",
            ["running"] = "1",
            ["blocking"] = "0",
        });

        var session = Assert.IsType<SessionSample>(ActivityRecordMapper.Map(row));

        Assert.Equal(
            new SessionSample(
                "microsoft.sql_server sql-prod-01",
                "production",
                _sample,
                117,
                "suspended",
                "running",
                "UPDATE",
                "shop",
                "app_shop",
                "web-22",
                "svc_shop",
                88,
                "LCK_M_U",
                134_000,
                "KEY: 5:72057594045726720 (8194443284a0)",
                134_200,
                12,
                94,
                5,
                64,
                1024,
                2048,
                GrantWaiting: true,
                TransactionMilliseconds: 4207,
                TransactionLogRecords: 2,
                OpenTransactions: 1,
                Isolation: 2,
                PercentComplete: 12.5,
                RemainingMilliseconds: 60_000,
                QueryHash: "0x6ACBDEE9D5014909",
                PlanHash: "0x88EB92735AB6DB89",
                IdleMilliseconds: 4207,
                ConnectedSeconds: 7440,
                Text: "UPDATE dbo.Orders SET Status = @status",
                Running: true,
                Blocking: false),
            session);
    }

    [Fact]
    public void Map_SessionWithoutTransactionOrMissingColumns_ReadsAsNoneAndZero()
    {
        var row = Row(new Dictionary<string, string> { ["session_id"] = "53", ["transaction_ms"] = "-1" });

        var session = Assert.IsType<SessionSample>(ActivityRecordMapper.Map(row));

        Assert.Null(session.TransactionMilliseconds);
        Assert.Equal("", session.WaitType);
        Assert.Equal(0, session.Reads);
    }

    [Fact]
    public void Map_SessionZero_IsTheServersFigures()
    {
        var row = Row(new Dictionary<string, string>
        {
            ["session_id"] = "0",
            ["tempdb_size_kb"] = "65536",
            ["tempdb_free_kb"] = "61440",
            ["grants_waiting"] = "3",
        });

        Assert.Equal(
            new ServerSample("microsoft.sql_server sql-prod-01", "production", _sample, 65_536, 61_440, 3),
            ActivityRecordMapper.Map(row));
    }

    [Fact]
    public void IsActivity_RecordWithoutAKnownKind_StaysALogRecord()
    {
        Assert.True(ActivityRecordMapper.IsActivity(Row([])));
        Assert.False(ActivityRecordMapper.IsActivity(Row([]) with { Attributes = new Dictionary<string, string>() }));
        Assert.False(ActivityRecordMapper.IsActivity(
            Row([]) with { Attributes = new Dictionary<string, string> { ["veyra_record"] = "something.else" } }));
    }

    private static LogRecord Row(Dictionary<string, string> columns)
    {
        columns["veyra_record"] = "sqlserver.session";
        columns["sample"] = _sample
            .ToUnixTimeMilliseconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new LogRecord(
            Timestamp: _sample.AddSeconds(1),
            ObservedTimestamp: _sample.AddSeconds(1),
            Severity: LogSeverity.Information,
            SeverityText: null,
            Body: "UPDATE dbo.Orders SET Status = @status",
            ServiceName: "microsoft.sql_server sql-prod-01",
            Environment: "production",
            TraceId: null,
            SpanId: null,
            Attributes: columns,
            ResourceAttributes: new Dictionary<string, string>(),
            BlockKind: AlertBlockKind.Database);
    }
}
