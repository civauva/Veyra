namespace Veyra.Agents.Api;

/// <summary>One change to an integration, from the settings history (ADR 0028).</summary>
/// <param name="At">When it was made.</param>
/// <param name="By">Who made it.</param>
/// <param name="Kind">
/// <c>added</c>, <c>changed</c>, <c>removed</c>, or <c>version</c> for a move to another template version.
/// </param>
/// <param name="From">The version before, for a version change.</param>
/// <param name="To">The version after, for a version change.</param>
public sealed record HistoryEntryResponse(DateTimeOffset At, string? By, string Kind, string? From, string? To);
