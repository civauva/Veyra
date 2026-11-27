namespace Veyra.Query.ServerOverview.Api;

/// <summary>One core counter, averaged over the window, with its change against the period before (ADR 0025).</summary>
/// <param name="Name"><c>batchRequests</c>, <c>compilations</c>, <c>pageLife</c>, or <c>waits</c>.</param>
/// <param name="Value">The average over the window; null without data.</param>
/// <param name="Unit"><c>per second</c>, <c>seconds</c>, or <c>ms per second</c>.</param>
/// <param name="Change">The change as a share of the period before; null without one.</param>
public sealed record CounterResponse(string Name, double? Value, string Unit, double? Change);
