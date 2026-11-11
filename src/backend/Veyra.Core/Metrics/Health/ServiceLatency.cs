namespace Veyra.Core.Metrics.Health;

/// <summary>How fast one service answered HTTP requests over the last five minutes.</summary>
/// <param name="Service">The service, from <c>service.name</c>.</param>
/// <param name="Environment">Its environment, or <see langword="null"/> when its metrics name none.</param>
/// <param name="P95Seconds">
/// The 95th percentile of its request durations; <see langword="null"/> without requests.
/// </param>
/// <param name="Requests">How many requests it answered.</param>
public sealed record ServiceLatency(string Service, string? Environment, double? P95Seconds, long Requests);
