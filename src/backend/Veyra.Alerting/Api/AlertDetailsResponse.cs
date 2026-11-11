using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;

namespace Veyra.Alerting.Api;

/// <summary>
/// One alert with its history and the measure of its rule: the error rate and the most frequent errors, a disk's use
/// with its forecast, or a service's latency with its slowest routes (ADR 0012).
/// </summary>
/// <param name="Id">The alert's ID.</param>
/// <param name="Rule">The rule that raised it, for example <c>error-rate</c> or <c>disk-filling</c>.</param>
/// <param name="Environment">The environment, if the sender named one.</param>
/// <param name="Block">What it is about: a service or a host, and the part of it.</param>
/// <param name="Service">The service, for service alerts; <see langword="null"/> for host alerts.</param>
/// <param name="Severity">The severity it has now, or had last when resolved.</param>
/// <param name="Worst">The highest severity it ever had.</param>
/// <param name="Value">The latest measurement, in the rule's unit.</param>
/// <param name="Peak">The worst measurement: the highest, or for days until full the lowest.</param>
/// <param name="PeakAt">When the peak was measured; unknown for alerts opened before Veyra recorded it.</param>
/// <param name="Threshold">The threshold of the current severity.</param>
/// <param name="CautionThreshold">The rule's caution threshold as configured now.</param>
/// <param name="WarningThreshold">The rule's warning threshold as configured now.</param>
/// <param name="ClearThreshold">Where a caution of the rule ends, as configured now.</param>
/// <param name="LowerIsWorse">Whether a lower measurement is worse, as for days until full.</param>
/// <param name="WindowSeconds">The rule's measuring window.</param>
/// <param name="MinimumRecords">
/// For the error-rate rule, the fewest records in the window before it judges a service; otherwise null.
/// </param>
/// <param name="MinimumRequests">
/// For the latency rule, the fewest requests in the window before it judges a service; otherwise null.
/// </param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="UpdatedAt">When it was last measured.</param>
/// <param name="ResolvedAt">When it resolved; <see langword="null"/> while open.</param>
/// <param name="Events">What happened, oldest first, each with the notifications it caused.</param>
/// <param name="Series">For the error-rate rule, the error rate around the alert; otherwise null.</param>
/// <param name="TopErrors">
/// For the error-rate rule, the service's most frequent error messages while it was open; otherwise empty.
/// </param>
/// <param name="Disk">For the disk rules, the disk's use and the host's other disks; otherwise null.</param>
/// <param name="Latency">For the latency rule, the request durations and the slowest routes; otherwise null.</param>
public sealed record AlertDetailsResponse(
    Guid Id,
    string Rule,
    string? Environment,
    AlertBlockResponse Block,
    string? Service,
    AlertSeverity Severity,
    AlertSeverity Worst,
    double Value,
    double Peak,
    DateTimeOffset? PeakAt,
    double Threshold,
    double CautionThreshold,
    double WarningThreshold,
    double ClearThreshold,
    bool LowerIsWorse,
    int WindowSeconds,
    int? MinimumRecords,
    int? MinimumRequests,
    DateTimeOffset OpenedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<AlertEventResponse> Events,
    AlertSeriesResponse? Series,
    IReadOnlyList<LogMessageGroup> TopErrors,
    DiskDetailsResponse? Disk,
    LatencyDetailsResponse? Latency);
