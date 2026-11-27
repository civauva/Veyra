namespace Veyra.Query.Jobs;

/// <summary>Which jobs the Jobs list keeps by how they stand (ADR 0027).</summary>
internal enum JobStatusFilter
{
    /// <summary>Every job.</summary>
    All,

    /// <summary>Jobs whose latest run failed.</summary>
    Failing,

    /// <summary>Jobs with a run going on now.</summary>
    Running,
}
