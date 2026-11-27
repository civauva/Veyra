import { Link, useParams, useSearchParams } from "react-router";

import type { JobDetails, JobListItem, JobRun } from "../../../api/types";
import { carryTime } from "../../../time/carryTime";
import {
  blockPath,
  blockWords,
  durationWords,
  logsPath,
  recordsWords,
  runLogsPath,
  sourceWords,
  statusTone,
  successWords,
} from "../../../jobs/jobWords";
import { useJob } from "../../../jobs/useJobs";
import { formatClockTime } from "../../../metrics/metricFormat";
import { shortCount } from "../../../resources/resourceFormat";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { SqlFigure } from "../database/sqlParts";
import { DurationBars, StatusPill, successFrame } from "./jobParts";
import "./consoleJobs.css";

/**
 * Console's job (VEY-69 reference screenshots 5 and 7, ADR 0027): its last run's state, the figures of the time
 * range, a chart of every run's duration, and the runs newest first. Picking a run shows its steps as an execution
 * path, its message, records, and retries, and leads to its trace and to the logs around it.
 */
export function ConsoleJob() {
  const { id = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const { details, notFound, error } = useJob(id);
  const list = { label: "Jobs & pipelines", to: carryTime("/jobs", params) };

  if (notFound) {
    return (
      <main className="cn-screen jb">
        <ScreenBar trail={[list, { label: "Not found" }]} />
        <p className="cn-empty">Veyra knows no such job. Its runs may be older than the 30 days they are kept.</p>
      </main>
    );
  }

  if (!details) {
    return (
      <main className="cn-screen jb">
        <ScreenBar trail={[list, { label: "…" }]} />
        <p className="cn-empty">{error ? `Could not read the job: ${error}` : "Reading the job…"}</p>
      </main>
    );
  }

  const { job, figures } = details;
  const chosen = details.runs.find((run) => run.runId === params.get("run")) ?? details.runs[0] ?? null;
  const block = blockPath(job.block);
  const windowLogs = logsPath(job, details.from, details.to);

  function pick(run: JobRun) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.set("run", run.runId);

        return next;
      },
      { replace: true },
    );
    // Below the table on narrower screens, the run's panel comes into view.
    window.requestAnimationFrame(() => document.getElementById("jb-run")?.scrollIntoView({ block: "nearest" }));
  }

  return (
    <main className="cn-screen jb">
      <ScreenBar trail={[list, { label: job.name }]} />
      <div className="cn-head">
        <div>
          <h1>
            {job.name}
            {job.running ? (
              <StatusPill status="running" />
            ) : job.lastRun ? (
              <span className={`cn-pill ${statusTone(job.lastRun.status)}`}>
                Last run {job.lastRun.status === "failed" ? "failed" : "succeeded"}
              </span>
            ) : null}
          </h1>
          <p className="jb-facts">
            <span className={`jb-source ${job.source}`}>{sourceWords(job.source)}</span>
            <span>
              on{" "}
              {block ? (
                <Link className="jb-block" to={block}>
                  {blockWords(job.block)}
                </Link>
              ) : (
                blockWords(job.block)
              )}
            </span>
            {job.environment && <span>{job.environment}</span>}
            {job.lastRun && <span>last run {formatClockTime(job.lastRun.startedAt)}</span>}
            <span>usually {durationWords(job.usualSeconds)}</span>
          </p>
        </div>
        <div className="cn-head-actions">
          {block && (
            <Link className="cn-button" to={block}>
              {job.block.kind === "service" ? "Open the service →" : "Open the server's jobs →"}
            </Link>
          )}
          {windowLogs && (
            <Link className="cn-button primary" to={windowLogs}>
              Open in Logs →
            </Link>
          )}
        </div>
      </div>
      {error && (
        <p className="cn-error" role="alert">
          Could not refresh: {error}
        </p>
      )}

      <section className="sq-figures jb-figures" aria-label="Figures">
        <SqlFigure
          label="Runs"
          value={figures.runs.toLocaleString("en")}
          detail="in the time range"
          help="Runs of this job that started in the time range, finished or still running."
        />
        <SqlFigure
          label="Failed"
          value={figures.failed.toLocaleString("en")}
          detail={job.lastRun?.status === "failed" ? "the last one too" : "runs that ended in error"}
          tone={figures.failed > 0 ? "bad" : undefined}
          help="Runs that failed: a root span with error status, or a SQL Server Agent run that did not succeed. Pick one in the runs below for its message and the step that failed."
        />
        <SqlFigure
          label="Success"
          value={successWords(figures.successRatio)}
          detail="of finished runs"
          tone={successFrame(figures.successRatio)}
          help="The share of finished runs that succeeded; runs going on now are not counted. Below 99% is amber, below 90% red."
        />
        <SqlFigure
          label="Median"
          value={durationWords(figures.medianSeconds)}
          detail="half the runs were faster"
          help="The duration half of the finished runs stayed under: what a run usually takes. The chart draws it as a dashed line."
        />
        <SqlFigure
          label="p95"
          value={durationWords(figures.p95Seconds)}
          detail="95% were faster"
          help="The duration 95 of 100 finished runs stayed under. Far above the median, some runs wait on something: a lock, a slow call, or a retry."
        />
        <SqlFigure
          label="Records"
          value={figures.recordsProcessed === null ? "—" : shortCount(figures.recordsProcessed)}
          detail={figures.recordsProcessed === null ? "not reported" : "processed in the range"}
          help="Records the runs processed, from records.processed or job.records.processed on the run's root span. Jobs that do not send it show a dash."
        />
      </section>

      <section className="cn-card" aria-labelledby="jb-durations">
        <header>
          <h2 id="jb-durations">Run durations</h2>
          <HelpTip label="Run durations">
            Every run of the time range at the time it started, as high as it took: green succeeded, red failed, blue
            still running. The dashed line is the median. A failure that is also much longer often timed out.
          </HelpTip>
          <span className="cn-meta">
            {figures.runs.toLocaleString("en")} {figures.runs === 1 ? "run" : "runs"}
          </span>
        </header>
        {details.durations.length === 0 ? (
          <p className="cn-empty">No run in the time range.</p>
        ) : (
          <DurationBars
            durations={details.durations}
            from={details.from}
            to={details.to}
            median={figures.medianSeconds}
            summary={`Durations of ${figures.runs} runs: median ${durationWords(figures.medianSeconds)}, p95 ${durationWords(figures.p95Seconds)}, ${figures.failed} failed.`}
          />
        )}
      </section>

      <div className="sq-row sq-row-main">
        <Runs details={details} chosen={chosen} onPick={pick} />
        <RunPanel job={job} run={chosen} />
      </div>
    </main>
  );
}

/** The runs of the time range, newest first; the chosen one is marked. */
function Runs({ details, chosen, onPick }: { details: JobDetails; chosen: JobRun | null; onPick(run: JobRun): void }) {
  return (
    <section className="cn-card" aria-labelledby="jb-runs">
      <header>
        <h2 id="jb-runs">Runs</h2>
        <HelpTip label="Runs">
          The runs of the time range, newest first, with how they ended, how long they took, the records they processed,
          and their retries. Pick one for its steps, its message, its trace, and the logs around it.
        </HelpTip>
      </header>
      {details.runs.length === 0 ? (
        <p className="cn-empty">No run in the time range.</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table jb-table">
            <thead>
              <tr>
                <th scope="col">Started</th>
                <th scope="col">Status</th>
                <th scope="col" className="num">
                  Duration
                </th>
                <th scope="col" className="num wide">
                  Records
                </th>
                <th scope="col" className="num wide">
                  Retries
                </th>
                <th scope="col" className="wide">
                  Message
                </th>
              </tr>
            </thead>
            <tbody>
              {details.runs.map((run) => {
                const picked = run.runId === chosen?.runId;

                return (
                  <tr key={run.runId} className={picked ? "jb-picked" : undefined} onClick={() => onPick(run)}>
                    <td>
                      <button
                        type="button"
                        className="jb-pick"
                        aria-pressed={picked}
                        onClick={(event) => {
                          event.stopPropagation();
                          onPick(run);
                        }}
                      >
                        {formatClockTime(run.startedAt)}
                      </button>
                    </td>
                    <td>
                      <StatusPill status={run.status} />
                    </td>
                    <td className="num">
                      {run.status === "running" ? (
                        <span className="sq-muted">running</span>
                      ) : (
                        durationWords(run.durationSeconds)
                      )}
                    </td>
                    <td className="num wide">{recordsWords(run.recordsProcessed, run.recordsTotal)}</td>
                    <td className={run.retries ? "num wide jb-retried" : "num wide sq-muted"}>{run.retries ?? "—"}</td>
                    <td className="wide jb-message" title={run.message ?? undefined}>
                      {run.message ?? <span className="sq-muted">—</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** The chosen run: its facts, its steps as an execution path, its message, and where to look further. */
function RunPanel({ job, run }: { job: JobListItem; run: JobRun | null }) {
  if (!run) {
    return (
      <section className="cn-card" id="jb-run" aria-labelledby="jb-run-title">
        <header>
          <h2 id="jb-run-title">Run</h2>
        </header>
        <p className="cn-empty">No run in the time range.</p>
      </section>
    );
  }

  const logs = runLogsPath(job, run);
  const share =
    run.recordsProcessed !== null && run.recordsTotal ? Math.min(1, run.recordsProcessed / run.recordsTotal) : null;
  // A step's bar is its share of the run, so the slow step stands out.
  const longest = Math.max(1e-9, run.durationSeconds ?? 0, ...run.steps.map((step) => step.durationSeconds ?? 0));

  return (
    <section className="cn-card" id="jb-run" aria-labelledby="jb-run-title">
      <header>
        <h2 id="jb-run-title">Run {formatClockTime(run.startedAt)}</h2>
        <HelpTip label="Run">
          One run: how it ended, its steps in the order they ran with how long each took, what it reported, and the
          records and retries its root span carried. A traced run links to its trace while the spans are kept, 7 days.
        </HelpTip>
        <span className="cn-meta mono" title={run.runId}>
          {run.runId.length > 12 ? `${run.runId.slice(0, 12)}…` : run.runId}
        </span>
      </header>
      <div className="sq-pad jb-run">
        <dl className="jb-run-facts">
          <div>
            <dt>Status</dt>
            <dd>
              <StatusPill status={run.status} />
            </dd>
          </div>
          <div>
            <dt>Duration</dt>
            <dd>{run.status === "running" ? "running" : durationWords(run.durationSeconds)}</dd>
          </div>
          <div>
            <dt>Retries</dt>
            <dd className={run.retries ? "jb-retried" : undefined}>{run.retries ?? "—"}</dd>
          </div>
          {(run.recordsProcessed !== null || run.recordsTotal !== null) && (
            <div className="jb-run-records">
              <dt>Records processed</dt>
              <dd>
                {recordsWords(run.recordsProcessed, run.recordsTotal)}
                {share !== null && (
                  <span
                    className={`jb-progress ${run.status === "failed" ? "bad" : run.status === "running" ? "info" : "ok"}`}
                    role="img"
                    aria-label={`${Math.round(share * 100)}% of the records`}
                  >
                    <i style={{ width: `${share * 100}%` }} />
                  </span>
                )}
              </dd>
            </div>
          )}
        </dl>

        <h3 className="cn-section">Steps</h3>
        {run.steps.length === 0 ? (
          <p className="sq-muted sq-small">The run reported no steps.</p>
        ) : (
          <ol className="jb-steps" aria-label="Steps">
            {run.steps.map((step, index) => (
              <li key={index} className={statusTone(step.status)}>
                <i className="jb-step-dot" aria-hidden="true" />
                <div className="jb-step-body">
                  <div className="jb-step-line">
                    <b>{step.name}</b>
                    <span className="jb-step-time">
                      {step.status === "running"
                        ? "running"
                        : step.status === "unknown" && step.durationSeconds === null
                          ? "did not run"
                          : durationWords(step.durationSeconds)}
                    </span>
                  </div>
                  {step.durationSeconds !== null && (
                    <span className="jb-step-bar" aria-hidden="true">
                      <i style={{ width: `${Math.max(1, (step.durationSeconds / longest) * 100)}%` }} />
                    </span>
                  )}
                  {step.message && <small>{step.message}</small>}
                </div>
              </li>
            ))}
          </ol>
        )}

        {run.message && (
          <>
            <h3 className="cn-section">Message</h3>
            <pre className="cn-code jb-code">
              {run.status === "failed" ? <span className="cn-code-bad">{run.message}</span> : run.message}
            </pre>
          </>
        )}

        <div className="jb-run-links">
          {run.traceId &&
            (run.traceKept ? (
              <Link className="cn-button" to={`/traces/${encodeURIComponent(run.traceId)}`}>
                Open the trace →
              </Link>
            ) : (
              <span className="jb-expired" title="Spans are kept 7 days; job runs are kept 30 days.">
                Trace expired
              </span>
            ))}
          {logs && (
            <Link className="cn-button" to={logs}>
              Logs around the run →
            </Link>
          )}
        </div>
      </div>
    </section>
  );
}
