namespace Veyra.Core.Metrics;

/// <summary>How a sum or histogram counts. The numbers are stored in the database.</summary>
public enum MetricTemporality
{
    /// <summary>Not set; gauges have none.</summary>
    Unspecified = 0,

    /// <summary>Each point counts only what happened since the previous point.</summary>
    Delta = 1,

    /// <summary>Each point counts everything since its start time.</summary>
    Cumulative = 2,
}
