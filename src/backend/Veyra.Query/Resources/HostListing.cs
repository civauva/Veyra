using Veyra.Query.Metrics;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>
/// The whole Hosts and containers list before it is cut into pages, and what the Overview reads of it (ADR 0023).
/// </summary>
/// <param name="Window">The window the figures cover, in the buckets of the small lines.</param>
/// <param name="Items">Every host and container the filters keep, sorted.</param>
/// <param name="Header">The header strip over all of them.</param>
/// <param name="Cpu">
/// The average CPU share in use of the hosts kept, per bucket start, from 0 to 1; a bucket no host measured is left
/// out. Containers are not in it.
/// </param>
/// <param name="Memory">The average memory share in use of those hosts, per bucket start.</param>
/// <param name="Disk">The share in use of the fullest disk of any of those hosts, per bucket start.</param>
internal sealed record HostListing(
    MetricWindow Window,
    IReadOnlyList<HostItemResponse> Items,
    HostListHeaderResponse Header,
    IReadOnlyDictionary<DateTimeOffset, double> Cpu,
    IReadOnlyDictionary<DateTimeOffset, double> Memory,
    IReadOnlyDictionary<DateTimeOffset, double> Disk);
