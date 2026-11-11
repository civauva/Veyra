using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>What a rule decided about one subject in one evaluation.</summary>
/// <param name="Rule">The rule's name.</param>
/// <param name="Environment">The subject's environment.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Severity">The severity its alert should have; <see langword="null"/> when none should be open.</param>
/// <param name="Value">
/// The measurement to record; <see langword="null"/> when the rule had too little to judge by, which keeps the last
/// one.
/// </param>
/// <param name="Threshold">The threshold of <paramref name="Severity"/>, in the rule's unit.</param>
/// <param name="Window">The window the rule measures over.</param>
internal sealed record RuleVerdict(
    string Rule,
    string? Environment,
    AlertSubject Subject,
    AlertSeverity? Severity,
    double? Value,
    double Threshold,
    TimeSpan Window);
