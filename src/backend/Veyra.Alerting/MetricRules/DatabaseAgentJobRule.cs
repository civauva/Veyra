using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>Decides the severity of a SQL Agent job's alert. Pure logic, tested without a database.</summary>
internal static class DatabaseAgentJobRule
{
    /// <summary>The window the rule measures over: the job's last run as reported in the last five minutes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the job's alert should have now.</summary>
    /// <param name="failed">Whether the job's last run failed.</param>
    /// <returns>A caution while the last run failed; <see langword="null"/> once a run succeeds.</returns>
    public static AlertSeverity? Evaluate(bool failed) => failed ? AlertSeverity.Caution : null;
}
