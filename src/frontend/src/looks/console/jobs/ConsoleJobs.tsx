import { Link, useSearchParams } from "react-router";

import type { JobListItem } from "../../../api/types";
import { carryTime } from "../../../time/carryTime";
import {
  blockPath,
  blockWords,
  durationWords,
  jobPath,
  sourceWords,
  successTone,
  successWords,
} from "../../../jobs/jobWords";
import { sourceChoices, statusChoices, useJobChoices } from "../../../jobs/useJobChoices";
import { useJobs } from "../../../jobs/useJobs";
import { formatClockTime } from "../../../metrics/metricFormat";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { SqlFigure } from "../database/sqlParts";
import { RunStrip, StatusPill, successFrame } from "./jobParts";
import "./consoleJobs.css";

/**
 * Console's jobs and pipelines (VEY-69 reference screenshot 5, ADR 0027): figures for the time range, then every job
 * with its last run, durations, success share, and a strip of its latest runs, failing ones first. The search, the
 * status, and the source are request parameters; the server filters, counts, and orders.
 */
export function ConsoleJobs() {
  const controls = useJobChoices();
  const { list, error } = useJobs(controls.choices);
  const [params] = useSearchParams();
  const figures = list?.figures ?? null;
  const filtered = controls.choices.q !== "" || controls.choices.status !== "all" || controls.choices.source !== null;

  return (
    <main className="cn-screen jb">
      <ScreenBar trail={[{ label: "Resources" }, { label: "Jobs & pipelines" }]} />
      <div className="cn-head">
        <div>
          <h1>
            Jobs &amp; pipelines
            {figures && figures.failed > 0 && (
              <span className="cn-pill bad">
                {figures.failed} failed {figures.failed === 1 ? "run" : "runs"}
              </span>
            )}
            {figures && figures.running > 0 && <span className="cn-pill info">{figures.running} running</span>}
          </h1>
          <p>
            Jobs recognised from traces, and SQL Server Agent jobs, with their runs in the time range. Failing ones come
            first.
          </p>
        </div>
      </div>

      <section className="sq-figures sq-figures-five" aria-label="Figures">
        <SqlFigure
          label="Jobs"
          onOpen={() => controls.setStatus("all")}
          value={figures?.jobs ?? "—"}
          detail={filtered ? "matching the filters" : "listed below"}
          help="The jobs listed below: root spans that carry a job name, such as job.name or cicd.pipeline.name, and SQL Server Agent jobs. A job is its source, block, environment, and name; runs are kept 30 days."
        />
        <SqlFigure
          label="Runs"
          value={figures ? figures.runs.toLocaleString("en") : "—"}
          detail="in the time range"
          help="Runs of these jobs that started in the time range, finished or still running."
        />
        <SqlFigure
          label="Failed"
          onOpen={() => controls.setStatus("failing")}
          value={figures ? figures.failed.toLocaleString("en") : "—"}
          detail="runs that ended in error"
          tone={figures && figures.failed > 0 ? "bad" : undefined}
          help="Runs that failed: a root span with error status, or a SQL Server Agent run that did not succeed. Open a job for its message and the step that failed."
        />
        <SqlFigure
          label="Success"
          value={successWords(figures?.successRatio ?? null)}
          detail="of finished runs"
          tone={successFrame(figures?.successRatio ?? null)}
          help="The share of finished runs that succeeded. Runs still going on are not counted. Below 99% is amber, below 90% red."
        />
        <SqlFigure
          label="Running"
          onOpen={() => controls.setStatus("running")}
          value={figures?.running ?? "—"}
          detail="runs going on now"
          help="Runs that started and have not ended yet. A traced run ends when its root span arrives."
        />
      </section>

      {error && (
        <p className="cn-error" role="alert">
          Could not refresh: {error}
        </p>
      )}

      <section className="cn-card" aria-labelledby="jb-list">
        <header>
          <h2 id="jb-list">Jobs</h2>
          <HelpTip label="Jobs">
            Every job with its last run, how long runs usually take, the share that succeeded, and its latest 20 runs
            from oldest to newest. Failing jobs come first, then by name. Open one for its runs and their steps.
          </HelpTip>
          {figures && (
            <span className="cn-meta">
              {figures.jobs} {figures.jobs === 1 ? "job" : "jobs"}
            </span>
          )}
        </header>
        <div className="cn-filters">
          <input
            type="search"
            className="cn-find"
            aria-label="Find a job"
            placeholder="Find a job, service, or server"
            value={controls.text}
            onChange={(event) => controls.setText(event.target.value)}
          />
          <div className="cn-seg" role="group" aria-label="Status">
            {statusChoices.map(([value, label]) => (
              <button
                key={value}
                type="button"
                aria-pressed={controls.choices.status === value}
                onClick={() => controls.setStatus(value)}
              >
                {label}
              </button>
            ))}
          </div>
          <div className="cn-seg" role="group" aria-label="Source">
            {sourceChoices.map(([value, label]) => (
              <button
                key={label}
                type="button"
                aria-pressed={controls.choices.source === value}
                onClick={() => controls.setSource(value)}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        {list === null ? (
          <p className="cn-empty">Reading the jobs…</p>
        ) : list.items.length === 0 ? (
          <p className="cn-empty">
            {filtered
              ? "No job matches the filters in the time range."
              : "No job ran in the time range. Jobs show once a root span carries job.name or cicd.pipeline.name, or a SQL Server is monitored."}
          </p>
        ) : (
          <div className="cn-table-scroll">
            <table className="cn-table jb-table">
              <thead>
                <tr>
                  <th scope="col">Job</th>
                  <th scope="col" className="wide roomy">
                    Source
                  </th>
                  <th scope="col" className="wide">
                    Runs on
                  </th>
                  <th scope="col">Last run</th>
                  <th scope="col" className="num">
                    Duration
                  </th>
                  <th scope="col" className="num wide roomy">
                    Usual
                  </th>
                  <th scope="col" className="num">
                    Success
                  </th>
                  <th scope="col" className="wide">
                    Latest runs
                  </th>
                  <th scope="col" className="num wide">
                    Runs
                  </th>
                  <th scope="col" className="num wide">
                    Failed
                  </th>
                </tr>
              </thead>
              <tbody>
                {list.items.map((job) => (
                  <Row key={job.id} job={job} params={params} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </main>
  );
}

/** One job in the table. */
function Row({ job, params }: { job: JobListItem; params: URLSearchParams }) {
  const block = blockPath(job.block);
  const last = job.lastRun;
  const share = successTone(job.successRatio);

  return (
    <tr className={last?.status === "failed" ? "jb-failing" : undefined}>
      <td>
        <Link className="cn-table-name" to={carryTime(jobPath(job.id), params)}>
          {job.name}
        </Link>
        <span className="cn-table-sub">{job.environment ?? "no environment"}</span>
      </td>
      <td className="wide roomy">
        <span className={`jb-source ${job.source}`}>{sourceWords(job.source)}</span>
      </td>
      <td className="wide">
        {block ? (
          <Link
            className="jb-block"
            to={block}
            aria-label={`${blockWords(job.block)} ${job.block.kind === "service" ? "service" : "server"}`}
          >
            {blockWords(job.block)}
          </Link>
        ) : (
          blockWords(job.block)
        )}
      </td>
      <td>
        {last ? (
          <span className="jb-last">
            <StatusPill status={last.status} />
            <span className="cn-table-sub">{formatClockTime(last.startedAt)}</span>
          </span>
        ) : (
          <span className="sq-muted">—</span>
        )}
      </td>
      <td className="num">
        {last?.status === "running" ? (
          <span className="sq-muted">running</span>
        ) : (
          durationWords(last?.durationSeconds ?? null)
        )}
      </td>
      <td className="num wide roomy sq-muted">{durationWords(job.usualSeconds)}</td>
      <td className="num">
        <span className={share ? `jb-share ${share}` : "jb-share"}>{successWords(job.successRatio)}</span>
      </td>
      <td className="wide">
        <RunStrip recent={job.recent} />
      </td>
      <td className="num wide">{job.runs.toLocaleString("en")}</td>
      <td className={job.failed > 0 ? "num wide jb-failed" : "num wide sq-muted"}>{job.failed.toLocaleString("en")}</td>
    </tr>
  );
}
