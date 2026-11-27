namespace Veyra.Query.Metrics.Api;

/// <summary>One of the statements with the most total time over the chosen range.</summary>
/// <param name="Text">The statement.</param>
/// <param name="TotalSeconds">Its total elapsed time over the range.</param>
/// <param name="Executions">How many times it ran.</param>
/// <param name="WorkerSeconds">Its CPU time.</param>
/// <param name="LogicalReads">Its pages read from memory.</param>
public sealed record TopQueryResponse(
    string Text,
    double TotalSeconds,
    long Executions,
    double? WorkerSeconds,
    long? LogicalReads)
{
    /// <summary>The database it ran in, when the agent said.</summary>
    public string? Database { get; init; }

    /// <summary>Whether it is the server's own housekeeping rather than an application's work (VEY-64).</summary>
    public bool System { get; init; }
}
