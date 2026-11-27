using Veyra.Core.Telemetry;

namespace Veyra.Core.Jobs;

/// <summary>
/// What a job is (ADR 0027): its source, the block it runs on, its environment, and its name. Two jobs of the same name
/// on two services, or in two environments, are two jobs.
/// </summary>
/// <param name="Source">Where its runs come from.</param>
/// <param name="Environment">Its environment, or <see langword="null"/> when the sender names none.</param>
/// <param name="Block">The service it runs in, or the database server whose agent runs it.</param>
/// <param name="Name">The job's name.</param>
public sealed record JobKey(JobSource Source, string? Environment, TelemetryBlock Block, string Name);
