using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Server;

/// <summary>
/// Builds a SQL Server's Events tab (ADR 0020, area 11) from the system_health events the agent sends.
/// </summary>
/// <param name="activity">The event records.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class EventsViewBuilder(IDatabaseActivitySearch activity, TimeProvider clock)
{
    /// <summary>The kind of record an event is.</summary>
    public const string EventKind = "sqlserver.event";

    /// <summary>Errors about memory that system_health records.</summary>
    public static readonly IReadOnlySet<int> MemoryErrors =
        new HashSet<int> { 701, 802, 8645, 8651, 8657, 8902, 17803 };

    /// <summary>
    /// Errors about damaged pages: reading failed, a checksum or page ID was wrong, or a read was retried.
    /// </summary>
    public static readonly IReadOnlySet<int> DamageErrors = new HashSet<int> { 823, 824, 825 };

    /// <summary>
    /// Errors about a client's connection that closed or broke while it logged in or sent a request (VEY-61). SQL
    /// Server rates them severity 20, but they say nothing of its data or health: usually a client gave up or timed
    /// out.
    /// </summary>
    public static readonly IReadOnlySet<int> ConnectionErrors = new HashSet<int> { 4014, 17830, 17832, 17835, 17836 };

    /// <summary>
    /// The events whose severity is not <c>ok</c>, by their name, for storage to read in the Simple view (VEY-68).
    /// It names the same events <see cref="Event"/> words as a caution or a warning; a test keeps the two alike.
    /// </summary>
    public static readonly ActivityAttributeMatch NeedsSomething = new(
        "event_name",
        ["error_reported", "wait_info", "wait_info_external", "security_error_ring_buffer_recorded"],
        ["non_yielding"]);

    /// <summary>The most events the tab reads.</summary>
    private const int _limit = 1_000;

    /// <summary>
    /// Whether an error rule counts an event (ADR 0020, VEY-61): severe errors of severity 20 and above or about
    /// damaged pages, but not a client's broken connection; memory errors; failed logins and permission checks.
    /// </summary>
    /// <param name="measure">
    /// The rule's measure: <c>severe-errors</c>, <c>memory-errors</c>, or <c>login-failures</c>.
    /// </param>
    /// <param name="shown">The event.</param>
    /// <returns><see langword="true"/> when the rule counts it.</returns>
    public static bool Counts(string measure, EventResponse shown)
    {
        ArgumentNullException.ThrowIfNull(shown);

        return measure switch
        {
            "severe-errors" => shown.Name == "error_reported"
                && !ConnectionErrors.Contains(shown.Error)
                && (shown.ErrorSeverity >= 20 || DamageErrors.Contains(shown.Error)),
            "memory-errors" => MemoryErrors.Contains(shown.Error),
            "login-failures" => shown.Name == "security_error_ring_buffer_recorded",
            _ => false,
        };
    }

    /// <summary>Builds the tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="simple">Whether to leave out the events that need nothing (VEY-67).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The events, the newest first.</returns>
    public async Task<IReadOnlyList<EventResponse>> EventsAsync(
        string engine,
        string server,
        MetricSpan span,
        bool simple,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var block = TelemetryBlock.Database(engine, server).Name;

        // Storage leaves out what needs nothing before the limit, so a fault behind many quiet events is still read.
        var records = simple
            ? await activity.ListRecordsMatchingAsync(
                block,
                EventKind,
                window.Start,
                window.End,
                NeedsSomething,
                _limit,
                cancellationToken)
            : await activity.ListRecordsAsync(
                block,
                EventKind,
                window.Start,
                window.End,
                null,
                _limit,
                cancellationToken);

        return Shown(records, simple);
    }

    /// <summary>Each event of the records once; without those that need nothing when simple. Pure.</summary>
    /// <param name="records">The event records, the newest first.</param>
    /// <param name="simple">Whether to leave out the events whose severity is <c>ok</c> (VEY-67).</param>
    /// <returns>The events, the newest first.</returns>
    public static IReadOnlyList<EventResponse> Shown(IReadOnlyList<ActivityRecord> records, bool simple)
    {
        ArgumentNullException.ThrowIfNull(records);

        return
        [
            .. records
                .GroupBy(record => record.Key)
                .Select(group => Event(group.First()))
                .Where(shown => !simple || shown.Severity != "ok"),
        ];
    }

    /// <summary>Words an event for anyone. Pure; tested without a database.</summary>
    /// <param name="record">The event.</param>
    /// <returns>The event with its words and severity.</returns>
    public static EventResponse Event(ActivityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var name = record.Attributes.GetValueOrDefault("event_name") ?? "";
        var error = (int)Number(record, "error_number");
        var severity = (int)Number(record, "severity");
        var wait = record.Attributes.GetValueOrDefault("wait_type") ?? "";
        var seconds = Number(record, "duration") / 1000;

        var (level, words) = name switch
        {
            "error_reported" when DamageErrors.Contains(error) =>
                (
                    "warning",
                    $"Error {error}: SQL Server read a damaged page. "
                    + "The disk or its controller may be failing."),
            "error_reported" when MemoryErrors.Contains(error) =>
                ("warning", $"Error {error}: SQL Server ran short of memory for a request."),
            "error_reported" when ConnectionErrors.Contains(error) =>
                (
                    "caution",
                    $"Error {error}: a client's connection closed while it logged in or sent a request. Usually the "
                    + "client gave up or timed out; many in a row point to the network or a client."),
            "error_reported" => (severity >= 20 ? "warning" : "caution", $"Error {error}, severity {severity}."),
            "wait_info" or "wait_info_external" =>
                ("caution", string.Create(CultureInfo.InvariantCulture, $"A request waited {seconds:0} s on {wait}.")),
            var other when other.Contains("non_yielding", StringComparison.Ordinal) =>
                (
                    "warning",
                    "A scheduler stopped yielding: SQL Server could not run anything else on that CPU for a while."),
            "security_error_ring_buffer_recorded" =>
                ("caution", $"A login or permission check failed (error {error})."),
            "connectivity_ring_buffer_recorded" => ("ok", "A connection was closed or failed at the network level."),
            _ => ("ok", name.Replace('_', ' ')),
        };

        return new EventResponse(record.SampledAt, name, level, words, error, severity, wait, record.Body);
    }

    private static double Number(ActivityRecord record, string key) =>
        double.TryParse(
            record.Attributes.GetValueOrDefault(key),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
