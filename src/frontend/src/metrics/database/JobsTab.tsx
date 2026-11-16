import { api } from "../../api/client";
import type { AgentJobView } from "../../api/types";
import { formatClockTime, formatSpan } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { clock } from "./liveFormat";

/** Props for {@link JobsTab}. */
export interface JobsTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/**
 * A SQL Server's Jobs tab (ADR 0020, area 10). The Simple view lists the jobs that need a look, failed, late, or
 * running long, with when each runs next; the Advanced view lists every job with its runs and steps of the last week.
 */
export function JobsTab({ engine, server, advanced }: JobsTabProps) {
  const { data, error } = useMetricData(`jobs-${engine}-${server}`, (signal) =>
    api.databaseJobs(engine, server, signal),
  );

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The jobs could not be loaded: {error}
      </p>
    ) : null;
  }

  const shown = advanced ? data : data.filter(needsLook);

  return (
    <section className="metric-panel" aria-labelledby="jobs-head">
      <div className="metric-panel-head">
        <h2 id="jobs-head">SQL Agent jobs</h2>
        <span>{advanced ? "every job, the ones that need a look first" : "the ones that need a look"}</span>
      </div>
      {data.length === 0 ? (
        <p className="metrics-empty">The server has no enabled SQL Agent job.</p>
      ) : shown.length === 0 ? (
        <p className="metrics-empty">Every job ran when it should, succeeded, and took its usual time.</p>
      ) : (
        <ul className="live-findings">
          {shown.map((job) => (
            <li
              key={job.name}
              className={`live-finding ${needsLook(job) ? (job.failed ? "warning" : "caution") : "ok"}`}
            >
              <i aria-hidden="true" />
              <div>
                <b>{job.name}</b>
                <p>{words(job)}</p>
                {advanced && job.runs.length > 0 && (
                  <details className="storage-database">
                    <summary>{job.runs.length === 1 ? "1 run" : `${job.runs.length} runs`} of the last week</summary>
                    <ul className="live-plain">
                      {job.runs.map((run) => (
                        <li key={run.at}>
                          <span>
                            <b>{formatClockTime(run.at)}</b> {run.succeeded ? "succeeded" : "failed"} in{" "}
                            {clock(run.seconds)}
                            {run.steps.length > 0 && (
                              <small className="faint">
                                {" · "}
                                {run.steps
                                  .map((step) => `${step.id} ${step.name}: ${step.succeeded ? "ok" : "failed"}`)
                                  .join(" · ")}
                              </small>
                            )}
                            {!run.succeeded && <small className="faint"> · {run.message}</small>}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </details>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** Whether a job needs a look: failed, late, or running at least twice its usual time. */
function needsLook(job: AgentJobView): boolean {
  return (
    job.failed ||
    job.late ||
    (job.runningSeconds !== null &&
      job.runningSeconds >= 300 &&
      job.usualSeconds !== null &&
      job.runningSeconds >= 2 * job.usualSeconds)
  );
}

/** A job in one sentence. */
function words(job: AgentJobView): string {
  const parts: string[] = [];

  if (job.runningSeconds !== null) {
    parts.push(
      `Running for ${formatSpan(job.runningSeconds)}${job.usualSeconds !== null ? `, usually ${formatSpan(job.usualSeconds)}` : ""}`,
    );
  } else if (job.lastRun) {
    parts.push(
      `Last run ${formatClockTime(job.lastRun)} ${job.failed ? "failed" : "succeeded"}${job.lastSeconds !== null ? ` in ${formatSpan(job.lastSeconds)}` : ""}`,
    );
  }

  if (job.late) {
    parts.push("it did not start when scheduled");
  }

  if (job.nextRun) {
    parts.push(`next run ${formatClockTime(job.nextRun)}`);
  }

  return parts.join("; ") + ".";
}
