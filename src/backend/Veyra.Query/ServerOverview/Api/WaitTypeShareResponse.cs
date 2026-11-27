namespace Veyra.Query.ServerOverview.Api;

/// <summary>One wait type's time over the window and its share of all waits (ADR 0025).</summary>
/// <param name="Type">The wait type, such as <c>PAGEIOLATCH_SH</c>, or <c>Other</c>.</param>
/// <param name="Category">Its category as the receiver names it, such as <c>Buffer IO</c>; null for Other.</param>
/// <param name="Seconds">The seconds waited.</param>
/// <param name="Share">Its share of the total, 0 to 1.</param>
public sealed record WaitTypeShareResponse(string Type, string? Category, double Seconds, double Share);
