using Veyra.Core.Activity;
using Veyra.Query.Metrics;
using Veyra.Query.Server;

namespace Veyra.Query.Tests.Metrics;

/// <summary>
/// The errors the error rules of a SQL Server count (ADR 0020, VEY-61): severe errors leave out a client's connection
/// that closed while it logged in, which SQL Server rates severity 20 but which is no fault of the server, and an
/// alert lists the errors it counted.
/// </summary>
public class ServerErrorTests
{
    private const string _server = "microsoft.sql_server PC-01";

    private static readonly DateTimeOffset _now = new(2026, 11, 25, 13, 16, 0, TimeSpan.Zero);

    private readonly FakeActivity _activity = new();

    [Fact]
    public async Task ListDatabaseMeasuresAsync_ConnectionClosedDuringLogin_IsNoSevereError()
    {
        _activity.Error(_now.AddMinutes(-4), 17830, 20, "Network error code 0xe8 occurred");

        var measures = await Health().ListDatabaseMeasuresAsync(_now, CancellationToken.None);

        Assert.Equal(0, measures.Single(measure => measure.Measure == "severe-errors").Value);
    }

    [Fact]
    public async Task ListDatabaseMeasuresAsync_DamagedPageAndAssertion_AreSevereErrors()
    {
        _activity.Error(_now.AddMinutes(-4), 824, 24, "SQL Server detected a logical consistency-based I/O error");
        _activity.Error(_now.AddMinutes(-3), 3624, 20, "A system assertion check has failed.");
        _activity.Error(_now.AddMinutes(-2), 17830, 20, "Network error code 0xe8 occurred");

        var measures = await Health().ListDatabaseMeasuresAsync(_now, CancellationToken.None);

        Assert.Equal(2, measures.Single(measure => measure.Measure == "severe-errors").Value);
    }

    [Fact]
    public async Task ListServerErrorsAsync_SevereErrors_ListsWhatTheRuleCountedWithTheirMessages()
    {
        _activity.Error(_now.AddMinutes(-4), 824, 24, "SQL Server detected a logical consistency-based I/O error");
        _activity.Error(_now.AddMinutes(-2), 17830, 20, "Network error code 0xe8 occurred");

        var errors = await Health().ListServerErrorsAsync(
            _server,
            "severe-errors",
            _now.AddMinutes(-15),
            _now,
            CancellationToken.None);

        var error = Assert.Single(errors);
        Assert.Equal(
            (_now.AddMinutes(-4), 824, 24, "SQL Server detected a logical consistency-based I/O error"),
            (error.At, error.Error, error.Severity, error.Message));
    }

    [Fact]
    public async Task ListServerErrorsAsync_FailedLogins_AreWordedWhenTheyHaveNoMessage()
    {
        _activity.Add(_now.AddMinutes(-1), "security_error_ring_buffer_recorded", 18456, 0, "");

        var error = Assert.Single(await Health().ListServerErrorsAsync(
            _server,
            "login-failures",
            _now.AddMinutes(-15),
            _now,
            CancellationToken.None));

        Assert.Equal((18456, "A login or permission check failed (error 18456)."), (error.Error, error.Message));
    }

    [Fact]
    public void Event_ConnectionClosedDuringLogin_IsACautionAboutTheClient()
    {
        var record = FakeActivity.Record(_now, "error_reported", 17830, 20, "Network error code 0xe8 occurred");

        var shown = EventsViewBuilder.Event(record);

        Assert.Equal("caution", shown.Severity);
        Assert.Contains("client's connection closed", shown.Words, StringComparison.Ordinal);
    }

    private MetricHealth Health() => new(new FakeMetricSearch(), _activity);

    /// <summary>One server that sent a live read a minute ago, and the events a test adds.</summary>
    private sealed class FakeActivity : IDatabaseActivitySearch
    {
        private readonly List<ActivityRecord> _records = [];

        public static ActivityRecord Record(DateTimeOffset at, string name, int error, int severity, string message) =>
            new(
                _server,
                "local",
                at,
                EventsViewBuilder.EventKind,
                $"{name}-{error}-{at:O}",
                new Dictionary<string, string>
                {
                    ["event_name"] = name,
                    ["error_number"] = error.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["severity"] = severity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                message);

        public void Error(DateTimeOffset at, int error, int severity, string message) =>
            Add(at, "error_reported", error, severity, message);

        public void Add(DateTimeOffset at, string name, int error, int severity, string message) =>
            _records.Add(Record(at, name, error, severity, message));

        public Task<SessionSnapshot?> GetSessionsAsync(
            string block,
            DateTimeOffset at,
            TimeSpan maxAge,
            CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public Task<IReadOnlyList<SessionSnapshot>> ListLatestSessionsAsync(
            DateTimeOffset now,
            TimeSpan maxAge,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSnapshot>>(
            [
                new SessionSnapshot(
                    _server,
                    "local",
                    now.AddMinutes(-1),
                    new ServerSample(_server, "local", now.AddMinutes(-1), 4_000_000, 1_000_000, 0),
                    []),
            ]);

        public Task<IReadOnlyList<ActivityRecord>> ListRecordsAsync(
            string block,
            string kind,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            IReadOnlyCollection<string>? keys,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActivityRecord>>(_records
                .Where(record => record.Block == block && record.Kind == kind)
                .Where(record => record.SampledAt >= periodStart && record.SampledAt <= periodEnd)
                .ToList());

        public async Task<IReadOnlyList<ActivityRecord>> ListRecordsMatchingAsync(
            string block,
            string kind,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            ActivityAttributeMatch match,
            int limit,
            CancellationToken cancellationToken) =>
            [
                .. (await ListRecordsAsync(block, kind, periodStart, periodEnd, null, limit, cancellationToken))
                    .Where(record => match.Matches(record.Attributes)),
            ];

        public Task<IReadOnlyList<SessionSample>> ListBlockingAsync(
            string block,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSample>>([]);

        public Task<IReadOnlyList<SessionCaller>> ListCallersAsync(
            string block,
            string queryHash,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionCaller>>([]);
    }
}
