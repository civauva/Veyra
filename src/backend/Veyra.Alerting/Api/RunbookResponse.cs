namespace Veyra.Alerting.Api;

/// <summary>The installation's own runbook for a rule, set on the Settings screen (ADR 0026).</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Url">The runbook.</param>
public sealed record RunbookResponse(string Rule, Uri Url);
