namespace Veyra.Query.Home;

/// <summary>
/// The expected load of one coming hour, learned from the same hour on previous days.
/// </summary>
/// <param name="Start">The start of the hour.</param>
/// <param name="Load">
/// The usual record count of this hour relative to an average hour, so 1.5 means half again as busy. Null while
/// Veyra is still learning, which is until the hour was seen on at least two days.
/// </param>
public sealed record ExpectedHourResponse(DateTimeOffset Start, double? Load);
