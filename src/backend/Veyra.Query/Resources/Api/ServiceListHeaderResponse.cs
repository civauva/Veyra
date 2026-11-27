namespace Veyra.Query.Resources.Api;

/// <summary>
/// The header strip of the Services list, over every service the filters keep. Each change compares the window with
/// the period of the same length before it (ADR 0022).
/// </summary>
/// <param name="Total">How many services.</param>
/// <param name="Healthy">How many have no open alert, silent ones included.</param>
/// <param name="Warning">How many have a warning open.</param>
/// <param name="Caution">How many have a caution open and no warning.</param>
/// <param name="Silent">How many sent no metrics for longer than <c>Veyra:Query:SilentAfterServices</c>.</param>
/// <param name="Requests">
/// HTTP requests served over the window, from <c>http.server.request.duration</c>; null when no service sends it.
/// </param>
/// <param name="RequestsChange">
/// The change of <paramref name="Requests"/> as a share: 0.12 is 12 % more; null when the period before has none.
/// </param>
/// <param name="ErrorRatio">The share of those requests answered with a 5xx status, from 0 to 1.</param>
/// <param name="ErrorRatioChange">The change of the ratio, in its own unit: 0.0012 is 0.12 percentage points.</param>
/// <param name="P95Seconds">
/// The 95th percentile of every request's duration, from the histograms whose bounds agree with the first.
/// </param>
/// <param name="P95Change">The change of the percentile as a share, as for <paramref name="RequestsChange"/>.</param>
/// <param name="Instances">The instances of every service, added up.</param>
/// <param name="Hosts">How many hosts the services run on, from <c>host.name</c> on their resources.</param>
public sealed record ServiceListHeaderResponse(
    int Total,
    int Healthy,
    int Warning,
    int Caution,
    int Silent,
    long? Requests,
    double? RequestsChange,
    double? ErrorRatio,
    double? ErrorRatioChange,
    double? P95Seconds,
    double? P95Change,
    int Instances,
    int Hosts);
