namespace Veyra.Agents.Agents;

/// <summary>
/// A Veyra agent: the official OpenTelemetry Collector, run and configured by Veyra over OpAMP (ADR 0019).
/// </summary>
/// <param name="Id">The agent's ID.</param>
/// <param name="Name">What the Agents page calls it, such as "Built in".</param>
/// <param name="Placement">Where it runs.</param>
/// <param name="IngestionKeyId">
/// The ingestion key it signs in with, to OpAMP and to OTLP; <see langword="null"/> for an agent added on the Agents
/// page that has not been installed yet.
/// </param>
/// <param name="CreatedAt">When it was set up.</param>
/// <param name="Report">What it last reported.</param>
internal sealed record Agent(
    Guid Id,
    string Name,
    AgentPlacement Placement,
    Guid? IngestionKeyId,
    DateTimeOffset CreatedAt,
    AgentReport Report);
