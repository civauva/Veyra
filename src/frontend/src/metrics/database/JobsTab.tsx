import { Link } from "react-router";

import { rulePath } from "../../alerts/rulePath";
import { api } from "../../api/client";
import { unreadText } from "./unread";
import type { AgentJobView } from "../../api/types";
import { formatClockTime, formatSpan } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { PanelHead } from "./helpNames";
import { clock } from "./liveFormat";
import { SystemHidden, useSystemShown } from "./SystemFilter";

/** Props for {@link JobsTab}. */
export interface JobsTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** Whether the login lacks a permission this tab needs, so an empty answer proves nothing (VEY-58). */
  blind?: boolean;
}

/**
 * A SQL Server's Jobs tab (ADR 0020, area 10). The Simple view lists the jobs that need a look, failed, late, or
 * running long, with when each runs next; the Advanced view lists every job with its runs and steps of the last week.
 */
export function JobsTab({ engine, server, advanced, blind = false }: JobsTabProps) {
  const [systemShown] = useSystemShown();
  // The server leaves out the jobs SQL Server set up for itself, such as syspolicy_purge_history, unless System is
  // on (VEY-64, VEY-66), and keeps the Simple view to the jobs that need a look (VEY-67).
  const { data, error } = useMetricData(
    `jobs-${engine}-${server}`,
    (signal) => api.databaseJobs(engine, server, systemShown, !advanced, signal),
    { choices: `${systemShown}-${advanced}` },
  );

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The jobs could not be loaded: {error}
      </p>
    ) : null;
  }

  const shown = data.jobs;

  return (
    <section className="metric-panel" aria-labelledby="jobs-head">
      <PanelHead
        id="jobs-head"
        name="SQL Agent jobs"
        help={
          "SQL Server Agent's enabled jobs, read every minute from msdb's job tables. A job " +
          "needs a look when its last run failed, it did not start when scheduled, or it " +
          "runs at least twice its usual time, the average of its last 10 successful runs. " +
          "Backups and maintenance usually run as jobs, so fix a failed one first."
        }
      >
        <span>{advanced ? "every job, the ones that need a look first" : "the ones that need a look"}</span>
      </PanelHead>
      {data.enabled === 0 && data.hiddenJobs.count === 0 ? (
        <p className="metrics-empty">{blind ? unreadText : "The server has no enabled SQL Agent job."}</p>
      ) : shown.length === 0 ? (
        <p className="metrics-empty">
          {blind ? unreadText : "Every job ran when it should, succeeded, and took its usual time."}
        </p>
      ) : (
        <ul className="live-findings">
          {shown.map((job) => (
            <li
              key={job.name}
              className={`live-finding ${job.needsLook ? (job.failed ? "warning" : "caution") : "ok"}`}
            >
              <i aria-hidden="true" />
              <div>
                <b>
                  <Link to={`/jobs?${new URLSearchParams({ source: "sqlagent", q: job.name }).toString()}`}>
                    {job.name}
                  </Link>
                </b>
                <p>
                  {words(job)}
                  {job.needsLook && (
                    <>
                      {" "}
                      <Link
                        to={`${rulePath(job.failed ? "db-agent-job" : job.late ? "db-job-late" : "db-job-overrun", {
                          kind: "database",
                          name: `${engine} ${server}`,
                          part: job.name,
                        })}#fix`}
                      >
                        How to fix it ›
                      </Link>
                    </>
                  )}
                </p>
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
      <SystemHidden hidden={data.hiddenJobs} noun="job" />
    </section>
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
