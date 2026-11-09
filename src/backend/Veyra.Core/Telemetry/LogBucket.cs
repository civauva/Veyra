namespace Veyra.Core.Telemetry;

/// <summary>
/// One bar of the log histogram: how many records fell into one interval.
/// </summary>
/// <param name="Start">The start of the interval.</param>
/// <param name="Total">All matching records in the interval.</param>
/// <param name="Errors">Matching records at <see cref="LogSeverity.Error"/> or worse.</param>
public sealed record LogBucket(DateTimeOffset Start, long Total, long Errors);
