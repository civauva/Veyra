using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>Decides the severity of one service's latency alert. Pure logic, tested without a database.</summary>
internal static class LatencyRule
{
    /// <summary>The window the rule measures over: the last five minutes, as on the Metrics screens.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the service's alert should have now.</summary>
    /// <remarks>
    /// With fewer requests than <see cref="LatencyOptions.MinimumRequests"/>, or no percentile at all, the rule
    /// cannot judge, so the current state is kept.
    /// </remarks>
    /// <param name="p95Seconds">The 95th percentile of its request durations.</param>
    /// <param name="requests">How many requests it answered in the window.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(
        double? p95Seconds,
        long requests,
        AlertSeverity? current,
        LatencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (p95Seconds is not { } p95 || !Judges(p95Seconds, requests, options))
        {
            return current;
        }

        return Thresholds.Rising(
            p95,
            current,
            options.CautionSeconds,
            options.WarningSeconds,
            threshold => threshold * options.ClearFactor);
    }

    /// <summary>Whether there is enough to judge the service by.</summary>
    /// <param name="p95Seconds">The 95th percentile of its request durations.</param>
    /// <param name="requests">How many requests it answered in the window.</param>
    /// <param name="options">The minimum.</param>
    /// <returns><see langword="true"/> when the rule can judge.</returns>
    public static bool Judges(double? p95Seconds, long requests, LatencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return p95Seconds is not null && requests >= options.MinimumRequests && requests > 0;
    }

    /// <summary>Returns the 95th percentile, in seconds, at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, LatencyOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningSeconds : options.CautionSeconds;
}
