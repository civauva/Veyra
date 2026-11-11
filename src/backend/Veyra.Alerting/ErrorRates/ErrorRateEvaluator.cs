using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;

namespace Veyra.Alerting.ErrorRates;

/// <summary>
/// Runs the built-in error-rate rule once over every service: opens, changes, and resolves its alerts.
/// </summary>
/// <param name="rates">The record and error counts per service, from the Storage module.</param>
/// <param name="store">Where alerts are kept.</param>
/// <param name="transitions">Applies the rule's verdicts to the alerts.</param>
/// <param name="options">The Alerting settings with the rule's thresholds.</param>
/// <param name="clock">The clock that sets the window's end and the alert times.</param>
internal sealed class ErrorRateEvaluator(
    ILogRates rates,
    AlertStore store,
    AlertTransitions transitions,
    IOptions<AlertingOptions> options,
    TimeProvider clock)
{
    /// <summary>Evaluates the rule as of now.</summary>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>A task that completes when every alert is up to date.</returns>
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var rule = options.Value.ErrorRate;

        if (!rule.Enabled)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var measured = (await rates.GetServiceRatesAsync(now - rule.Window, now, cancellationToken))
            .ToDictionary(rate => new ServiceKey(rate.Environment, rate.ServiceName));
        var open = (await store.ListOpenAsync(ErrorRateRule.Name, cancellationToken))
            .Where(alert => alert.Subject.Kind == AlertBlockKind.Service)
            .ToDictionary(alert => new ServiceKey(alert.Environment, alert.Subject.Name));

        // A service with an open alert but no records in the window is still evaluated, with zero records, so its
        // alert is kept rather than forgotten.
        foreach (var key in measured.Keys.Union(open.Keys))
        {
            var rate = measured.GetValueOrDefault(key) ?? new ServiceLogRate(key.Environment, key.ServiceName, 0, 0);
            var current = open.GetValueOrDefault(key);
            var next = ErrorRateRule.Evaluate(rate.Total, rate.Errors, current?.Severity, rule);
            var judged = rate.Total >= rule.MinimumRecords && rate.Total > 0;

            var verdict = new RuleVerdict(
                ErrorRateRule.Name,
                rate.Environment,
                AlertSubject.Service(rate.ServiceName),
                next,
                judged ? rate.ErrorRatio : null,
                next is { } severity ? ErrorRateRule.ThresholdFor(severity, rule) : 0,
                rule.Window);

            await transitions.ApplyAsync(verdict, current, now, cancellationToken);
        }
    }

    private sealed record ServiceKey(string? Environment, string ServiceName);
}
