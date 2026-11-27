using Veyra.Core.Jobs;

namespace Veyra.Query.Jobs;

/// <summary>
/// A SQL Server Agent job that runs now, from the agent's job metrics (ADR 0020, area 10). Its run is not in the job
/// history until it ends, so this is how the Jobs screens and the Overview know of it.
/// </summary>
/// <param name="Job">The job.</param>
/// <param name="Seconds">How long it has run so far.</param>
internal sealed record RunningJob(JobKey Job, double Seconds);
