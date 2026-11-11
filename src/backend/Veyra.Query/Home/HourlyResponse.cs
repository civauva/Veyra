using Veyra.Core.Telemetry;

namespace Veyra.Query.Home;

/// <summary>
/// The hourly strip: what happened in the past hours and what the coming hours usually bring.
/// </summary>
/// <param name="Observed">The past 23 hours and the current one, oldest first, with records and errors.</param>
/// <param name="Expected">The next six hours.</param>
/// <param name="DaysOfHistory">How many days of records the expectation is learned from, at most seven.</param>
public sealed record HourlyResponse(
    IReadOnlyList<LogBucket> Observed,
    IReadOnlyList<ExpectedHourResponse> Expected,
    int DaysOfHistory);
