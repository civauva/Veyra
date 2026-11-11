namespace Veyra.Core.Metrics.Health;

/// <summary>
/// The figures alert rules judge metrics by: disks and request latency (ADR 0012). The Query module implements it with
/// the code behind the Metrics screens and the home, so an alert and the screen it links to agree; the Alerting module
/// reads it.
/// </summary>
public interface IMetricHealth
{
    /// <summary>Lists every watched disk of every host that sent host metrics within the last 15 minutes.</summary>
    /// <remarks>
    /// Pseudo and read-only file systems are left out, and a device mounted at several places is one disk.
    /// </remarks>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The disks, in no particular order.</returns>
    Task<IReadOnlyList<DiskHealth>> ListDisksAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Measures the request latency of every service that sent request durations within the last 15 minutes, over the
    /// last five minutes.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One entry per service and environment, in no particular order.</returns>
    Task<IReadOnlyList<ServiceLatency>> ListLatenciesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Reads the share of one disk in use over a period.</summary>
    /// <param name="host">The host.</param>
    /// <param name="mountpoint">The disk's mount point.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The history; without points when the disk sent nothing in the period.</returns>
    Task<DiskHistory> ReadDiskAsync(
        string host,
        string mountpoint,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);

    /// <summary>Reads a service's request durations over a period.</summary>
    /// <param name="service">The service.</param>
    /// <param name="environment">Its environment, or <see langword="null"/> for every environment.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The history; without points when the service sent nothing in the period.</returns>
    Task<LatencyHistory> ReadLatencyAsync(
        string service,
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}
