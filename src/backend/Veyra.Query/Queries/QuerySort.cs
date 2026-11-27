namespace Veyra.Query.Queries;

/// <summary>
/// What the Queries tab ranks the statements by (VEY-66). The server ranks them before it keeps the top 100, so the
/// statement that reads the most is listed even when others took more time.
/// </summary>
public enum QuerySort
{
    /// <summary>All runs' time together, the default.</summary>
    TotalTime,

    /// <summary>The CPU time of all runs.</summary>
    Cpu,

    /// <summary>The logical reads of all runs.</summary>
    Reads,

    /// <summary>How many times the statement ran.</summary>
    Runs,

    /// <summary>The average time of one run.</summary>
    TimePerRun,
}
