namespace Veyra.Query.Home;

/// <summary>How many of the open alerts the home tells are at each severity (VEY-68).</summary>
/// <param name="Warnings">The warnings.</param>
/// <param name="Cautions">The cautions.</param>
public sealed record AlertCountsResponse(int Warnings, int Cautions);
