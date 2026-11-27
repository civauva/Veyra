namespace Veyra.Core.Jobs;

/// <summary>Where Veyra learned of a job's runs (ADR 0027).</summary>
public enum JobSource
{
    /// <summary>A root span that carries a job name attribute, from an application's own traces.</summary>
    Trace,

    /// <summary>SQL Server Agent's job history, read by Veyra's agent (ADR 0020).</summary>
    SqlAgent,
}
