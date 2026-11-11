using Veyra.Core.Alerts;

namespace Veyra.Alerting.Api;

/// <summary>One day of the Alerts screen's weather log.</summary>
/// <param name="Date">The day, in UTC.</param>
/// <param name="Count">The alerts that opened that day; alerts open before the period count on its first day.</param>
/// <param name="Worst">The worst severity among them; <see langword="null"/> for a day without alerts.</param>
public sealed record AlertDayResponse(DateOnly Date, int Count, AlertSeverity? Worst);
