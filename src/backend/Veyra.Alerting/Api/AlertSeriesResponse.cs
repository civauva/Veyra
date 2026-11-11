namespace Veyra.Alerting.Api;

/// <summary>
/// The error rate of the alert's service around the alert: from 30 minutes before it opened to 10 minutes after it
/// resolved, or to now.
/// </summary>
/// <param name="BucketSeconds">The width of each point: one minute, wider for long alerts.</param>
/// <param name="From">The start of the first point.</param>
/// <param name="To">The end of the last point.</param>
/// <param name="Points">One point per bucket, oldest first.</param>
public sealed record AlertSeriesResponse(
    int BucketSeconds,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<AlertPointResponse> Points);
