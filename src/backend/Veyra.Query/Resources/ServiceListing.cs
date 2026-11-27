using Veyra.Query.Metrics;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>
/// The whole Services list before it is cut into pages, and what the Overview reads of it (ADR 0023).
/// </summary>
/// <param name="Window">The window the figures cover, in the buckets of the small lines.</param>
/// <param name="Items">Every service the filters keep, sorted.</param>
/// <param name="Header">The header strip over all of them.</param>
/// <param name="Requests">
/// The HTTP requests every service served per bucket start of the window, added up; a bucket no service measured is
/// left out.
/// </param>
/// <param name="Errors">Those answered with a 5xx status per bucket start; a bucket without any is left out.</param>
internal sealed record ServiceListing(
    MetricWindow Window,
    IReadOnlyList<ServiceItemResponse> Items,
    ServiceListHeaderResponse Header,
    IReadOnlyDictionary<DateTimeOffset, double> Requests,
    IReadOnlyDictionary<DateTimeOffset, double> Errors);
