namespace Veyra.Core.Telemetry;

/// <summary>
/// How many records a service wrote in a time window, for the service filter.
/// </summary>
/// <param name="Name">The service name.</param>
/// <param name="Count">The number of matching records.</param>
public sealed record ServiceCount(string Name, long Count);
