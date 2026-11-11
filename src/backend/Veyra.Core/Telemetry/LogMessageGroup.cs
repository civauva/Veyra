namespace Veyra.Core.Telemetry;

/// <summary>
/// Records with exactly the same message, counted together: the most frequent errors of an alert.
/// </summary>
/// <param name="Message">The message the records share.</param>
/// <param name="Count">How many records have it.</param>
/// <param name="First">The time of the oldest of them.</param>
/// <param name="Last">The time of the newest of them.</param>
public sealed record LogMessageGroup(string Message, long Count, DateTimeOffset First, DateTimeOffset Last);
