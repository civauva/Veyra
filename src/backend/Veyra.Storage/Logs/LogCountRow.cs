using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;

namespace Veyra.Storage.Logs;

/// <summary>The records and errors of one block in one bucket, as counted by <see cref="LogCounts"/>.</summary>
/// <param name="Bucket">The clock-aligned start of the bucket, or <see langword="null"/> without buckets.</param>
/// <param name="Environment">The environment, or <see langword="null"/> for records without one.</param>
/// <param name="ServiceName">The name of the block: for a service, the service.</param>
/// <param name="Total">The records.</param>
/// <param name="Errors">The records at Error or worse.</param>
/// <param name="Kind">The kind of block (ADR 0018).</param>
/// <param name="Severity">The severity of the records, so the chart can stack each level (ADR 0024).</param>
internal sealed record LogCountRow(
    DateTimeOffset? Bucket,
    string? Environment,
    string ServiceName,
    long Total,
    long Errors,
    AlertBlockKind Kind = AlertBlockKind.Service,
    LogSeverity Severity = LogSeverity.Unspecified);
