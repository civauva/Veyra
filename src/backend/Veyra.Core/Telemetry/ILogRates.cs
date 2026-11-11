namespace Veyra.Core.Telemetry;

/// <summary>
/// Counts records and errors per service over a time window. The Storage module implements it; the Alerting module
/// evaluates its rules with it and the Query module builds the home snapshot with it, neither referencing Storage.
/// </summary>
public interface ILogRates
{
    /// <summary>
    /// Counts the records of every service that wrote at least one record in the window.
    /// </summary>
    /// <param name="windowStart">The start of the window, inclusive.</param>
    /// <param name="windowEnd">The end of the window, exclusive.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>One entry per environment and service, in no particular order.</returns>
    Task<IReadOnlyList<ServiceLogRate>> GetServiceRatesAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);
}
