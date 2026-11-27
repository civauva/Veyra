namespace Veyra.Query.Jobs.Api;

/// <summary>One step of a run.</summary>
/// <param name="Name">The step's name.</param>
/// <param name="DurationSeconds">How long it took; null when not known.</param>
/// <param name="Status"><c>succeeded</c>, <c>failed</c>, <c>running</c>, or <c>unknown</c>.</param>
/// <param name="Message">What it said about how it ended, such as an error; null for nothing.</param>
public sealed record JobRunStepResponse(string Name, double? DurationSeconds, string Status, string? Message);
