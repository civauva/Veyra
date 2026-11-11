namespace Veyra.Core.Telemetry;

/// <summary>
/// How many records one service wrote in a time window, and how many of them were errors.
/// </summary>
/// <param name="Environment">The deployment environment, or <see langword="null"/> when the sender set none.</param>
/// <param name="ServiceName">The service name.</param>
/// <param name="Total">All records in the window.</param>
/// <param name="Errors">Records at <see cref="LogSeverity.Error"/> or worse.</param>
public sealed record ServiceLogRate(string? Environment, string ServiceName, long Total, long Errors)
{
    /// <summary>The share of records that are errors, from 0 to 1, or 0 when there are no records.</summary>
    public double ErrorRatio => Total == 0 ? 0 : (double)Errors / Total;
}
