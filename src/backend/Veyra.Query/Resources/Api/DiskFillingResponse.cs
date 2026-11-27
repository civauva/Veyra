namespace Veyra.Query.Resources.Api;

/// <summary>The disk that is full soonest at the pace of the last day.</summary>
/// <param name="Host">The host.</param>
/// <param name="Mount">Where the disk is mounted, such as <c>/</c> or <c>D:</c>.</param>
/// <param name="HoursLeft">How many hours until it is full, rounded.</param>
public sealed record DiskFillingResponse(string Host, string Mount, double HoursLeft);
