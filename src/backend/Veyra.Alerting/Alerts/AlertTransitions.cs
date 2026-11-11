using Microsoft.Extensions.Logging;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Applies a rule's verdict on one subject to its alert: opens one, changes its severity and measurement, or resolves
/// it. Every rule decides the severity its own way and leaves the bookkeeping to this class.
/// </summary>
/// <param name="store">Where alerts are kept.</param>
/// <param name="logger">Receives every alert that opens, changes severity, or resolves.</param>
internal sealed partial class AlertTransitions(AlertStore store, ILogger<AlertTransitions> logger)
{
    /// <summary>Brings the subject's alert in line with the rule's verdict.</summary>
    /// <param name="verdict">What the rule decided.</param>
    /// <param name="current">
    /// The subject's open alert of this rule, or <see langword="null"/> when none is open.
    /// </param>
    /// <param name="now">The time of the evaluation.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes when the alert is up to date.</returns>
    public async Task ApplyAsync(
        RuleVerdict verdict,
        AlertState? current,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        var subject = Describe(verdict.Subject);

        if (current is null)
        {
            if (verdict.Severity is { } severity && verdict.Value is { } value)
            {
                var alert = new AlertState(
                    Id: Guid.CreateVersion7(now),
                    Rule: verdict.Rule,
                    Environment: verdict.Environment,
                    Subject: verdict.Subject,
                    Severity: severity,
                    Value: value,
                    Peak: value,
                    Threshold: verdict.Threshold,
                    Window: verdict.Window,
                    OpenedAt: now,
                    UpdatedAt: now,
                    ResolvedAt: null);

                await store.InsertAsync(alert, cancellationToken);
                LogOpened(verdict.Rule, severity, subject, verdict.Environment, value);
            }

            return;
        }

        if (verdict.Severity is null)
        {
            var value = verdict.Value ?? current.Value;

            await store.ResolveAsync(current.Id, value, now, cancellationToken);
            LogResolved(verdict.Rule, subject, verdict.Environment, value);

            return;
        }

        // Too little to judge by: keep the last real measurement instead of recording a meaningless one.
        var measured = verdict.Value ?? current.Value;

        await store.UpdateAsync(
            current.Id,
            verdict.Severity.Value,
            measured,
            verdict.Threshold,
            now,
            cancellationToken);

        if (verdict.Severity != current.Severity)
        {
            LogSeverityChanged(
                verdict.Rule,
                subject,
                verdict.Environment,
                current.Severity,
                verdict.Severity.Value,
                measured);
        }
    }

    private static string Describe(AlertSubject subject) =>
        subject.Part is null ? subject.Name : $"{subject.Part} on {subject.Name}";

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Rule} {Severity} opened for {Subject} in {Environment} at {Value}")]
    private partial void LogOpened(
        string rule,
        AlertSeverity severity,
        string subject,
        string? environment,
        double value);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Rule} alert for {Subject} in {Environment} changed from {From} to {To} at {Value}")]
    private partial void LogSeverityChanged(
        string rule,
        string subject,
        string? environment,
        AlertSeverity from,
        AlertSeverity to,
        double value);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{Rule} alert for {Subject} in {Environment} resolved at {Value}")]
    private partial void LogResolved(string rule, string subject, string? environment, double value);
}
