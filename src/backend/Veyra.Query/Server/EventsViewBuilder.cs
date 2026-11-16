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

    /// <summary>Builds the tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The events, the newest first.</returns>
    public async Task<IReadOnlyList<EventResponse>> EventsAsync(
        string engine,
        string server,
        string? range,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(range, clock.GetUtcNow());
        var records = await activity.ListRecordsAsync(
            TelemetryBlock.Database(engine, server).Name,
            EventKind,
            window.Start,
            window.End,
            null,
            1_000,
            cancellationToken);

        return records.GroupBy(record => record.Key).Select(group => Event(group.First())).ToList();
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
