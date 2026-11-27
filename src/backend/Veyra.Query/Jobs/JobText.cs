using Veyra.Core.Jobs;

namespace Veyra.Query.Jobs;

/// <summary>How the jobs API spells sources and statuses (ADR 0027), in lower case as the screens read them.</summary>
internal static class JobText
{
    /// <summary>The name of a source.</summary>
    /// <param name="source">The source.</param>
    /// <returns><c>trace</c> or <c>sqlagent</c>.</returns>
    public static string Source(JobSource source) => source == JobSource.SqlAgent ? "sqlagent" : "trace";

    /// <summary>Reads the name of a source, in any case.</summary>
    /// <param name="name"><c>trace</c> or <c>sqlagent</c>.</param>
    /// <returns>The source, or null for any other name.</returns>
    public static JobSource? SourceOf(string? name) => name?.ToLowerInvariant() switch
    {
        "trace" => JobSource.Trace,
        "sqlagent" => JobSource.SqlAgent,
        _ => null,
    };

    /// <summary>The name of a run's or step's status.</summary>
    /// <param name="status">The status, or null when it is not known.</param>
    /// <returns><c>succeeded</c>, <c>failed</c>, <c>running</c>, or <c>unknown</c>.</returns>
    public static string Status(JobRunStatus? status) => status switch
    {
        JobRunStatus.Succeeded => "succeeded",
        JobRunStatus.Failed => "failed",
        JobRunStatus.Running => "running",
        _ => "unknown",
    };
}
