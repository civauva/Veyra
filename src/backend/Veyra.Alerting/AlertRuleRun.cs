using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.ErrorRates;
using Veyra.Alerting.MetricRules;

namespace Veyra.Alerting;

/// <summary>
/// One run of every built-in rule: drops alerts that resolved longer ago than the history is kept, then evaluates the
/// error-rate rule and the metric rules.
/// </summary>
/// <param name="store">Where alerts are kept.</param>
/// <param name="errorRates">The error-rate rule.</param>
/// <param name="metrics">The metric rules.</param>
/// <param name="options">The Alerting settings with the history length.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class AlertRuleRun(
    AlertStore store,
    ErrorRateEvaluator errorRates,
    MetricRuleEvaluator metrics,
    IOptions<AlertingOptions> options,
    TimeProvider clock)
{
    /// <summary>Runs every rule once.</summary>
    /// <remarks>
    /// The error-rate rule runs before the metric rules, so a failing metric read does not hold it back; the failure
    /// still ends the run and is logged by <see cref="AlertEvaluation"/>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when every alert is up to date.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var history = TimeSpan.FromDays(options.Value.HistoryDays);

        await store.DeleteResolvedBeforeAsync(clock.GetUtcNow() - history, cancellationToken);
        await errorRates.EvaluateAsync(cancellationToken);
        await metrics.EvaluateAsync(cancellationToken);
    }
}
