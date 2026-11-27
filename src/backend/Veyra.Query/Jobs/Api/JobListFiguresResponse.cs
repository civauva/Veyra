namespace Veyra.Query.Jobs.Api;

/// <summary>What the jobs of the list add up to over the window.</summary>
/// <param name="Jobs">How many jobs there are.</param>
/// <param name="Runs">How many of their runs started in the window.</param>
/// <param name="Failed">How many of them failed.</param>
/// <param name="SuccessRatio">The share of their finished runs that succeeded; null without any.</param>
/// <param name="Running">How many of the jobs run now.</param>
public sealed record JobListFiguresResponse(int Jobs, int Runs, int Failed, double? SuccessRatio, int Running);
