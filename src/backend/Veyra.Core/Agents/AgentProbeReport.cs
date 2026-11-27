namespace Veyra.Core.Agents;

/// <summary>
/// The answer of a connection probe, as an agent sends it: one log record whose <c>veyra_record</c> attribute is
/// <see cref="Kind"/> and whose other attributes are the probe's ID and what it read (ADR 0028).
/// </summary>
/// <param name="KeyId">
/// The ingestion key the record came with. Only the agent the probe was given to may answer it, which this tells.
/// </param>
/// <param name="Attributes">The record's attributes, the columns of the probe's read.</param>
/// <param name="At">When the record was read.</param>
public sealed record AgentProbeReport(Guid? KeyId, IReadOnlyDictionary<string, string> Attributes, DateTimeOffset At)
{
    /// <summary>The <c>veyra_record</c> value of a probe's answer.</summary>
    public const string Kind = "sqlserver.probe";
}
