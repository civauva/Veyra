import { Link, useParams, useSearchParams } from "react-router";

import { formatClockTime } from "../metrics/metricFormat";
import { rangeOf, withRange } from "../metrics/metricRange";
import { RangePicker } from "../metrics/RangePicker";
import { ScreenBar } from "../ui/ScreenBar";
import {
  blockPath,
  blockWords,
  durationWords,
  recordsWords,
  runLogsPath,
  sourceWords,
  statusWords,
  successWords,
} from "./jobWords";
import { useJob } from "./useJobs";
import "./jobs.css";

/**
 * One job (ADR 0027) as every look shows it by default: its figures and its runs, newest first, each with its steps,
 * records, retries, message, trace, and logs. The Console look draws its own, with a chart of the durations.
 */
export function JobScreen() {
  const { id = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const { details, notFound, error } = useJob(id);
  const range = (
    <RangePicker
      value={rangeOf(params)}
      onChange={(next) => setParams((current) => withRange(current, next), { replace: true })}
    />
  );

  if (notFound) {
    return (
      <main className="jobs">
        <ScreenBar trail={[{ label: "Jobs", to: "/jobs" }, { label: "Not found" }]} />
        <p>Veyra knows no such job. Runs are kept 30 days.</p>
      </main>
    );
  }

  if (!details) {
    return (
      <main className="jobs">
        <ScreenBar trail={[{ label: "Jobs", to: "/jobs" }, { label: "…" }]}>{range}</ScreenBar>
        <p>{error ? `Could not read the job: ${error}` : "Reading the job…"}</p>
      </main>
    );
  }

  const { job, figures } = details;
  const block = blockPath(job.block);

  return (
    <main className="jobs">
      <ScreenBar trail={[{ label: "Jobs", to: "/jobs" }, { label: job.name }]}>{range}</ScreenBar>
      <h1>{job.name}</h1>
      <p className="jobs-lead">
        {sourceWords(job.source)} · on {block ? <Link to={block}>{blockWords(job.block)}</Link> : blockWords(job.block)}
        {job.environment ? ` · ${job.environment}` : ""}
      </p>
      {error && (
        <p className="jobs-error" role="alert">
          Could not refresh: {error}
        </p>
      )}
      <dl className="jobs-facts">
        <div>
          <dt>Runs</dt>
          <dd>{figures.runs}</dd>
        </div>
        <div>
          <dt>Failed</dt>
          <dd>{figures.failed}</dd>
        </div>
        <div>
          <dt>Succeeded</dt>
          <dd>{successWords(figures.successRatio)}</dd>
        </div>
        <div>
          <dt>Median</dt>
          <dd>{durationWords(figures.medianSeconds)}</dd>
        </div>
        <div>
          <dt>p95</dt>
          <dd>{durationWords(figures.p95Seconds)}</dd>
        </div>
      </dl>
      <h2>Runs</h2>
      {details.runs.length === 0 ? (
        <p>No run in the time range.</p>
      ) : (
        <ul className="jobs-list">
          {details.runs.map((run) => {
            const logs = runLogsPath(job, run);

            return (
              <li key={run.runId} className={run.status}>
                <b>
                  {formatClockTime(run.startedAt)} · {statusWords(run.status)} · {durationWords(run.durationSeconds)}
                </b>
                {run.message && <small>{run.message}</small>}
                <small>
                  {run.recordsProcessed !== null || run.recordsTotal !== null
                    ? `Records ${recordsWords(run.recordsProcessed, run.recordsTotal)} · `
                    : ""}
                  {run.retries ? `${run.retries} retries · ` : ""}
                  {run.steps.map((step) => `${step.name} ${statusWords(step.status).toLowerCase()}`).join(" → ")}
                </small>
                <small className="jobs-links">
                  {run.traceId &&
                    (run.traceKept ? <Link to={`/traces/${run.traceId}`}>Open the trace</Link> : "Trace expired")}
                  {logs && <Link to={logs}>Logs around the run</Link>}
                </small>
              </li>
            );
          })}
        </ul>
      )}
    </main>
  );
}
