import { Link, useSearchParams } from "react-router";

import { formatClockTime } from "../metrics/metricFormat";
import { rangeOf, withRange } from "../metrics/metricRange";
import { RangePicker } from "../metrics/RangePicker";
import { ScreenBar } from "../ui/ScreenBar";
import { blockWords, durationWords, jobPath, sourceWords, statusWords, successWords } from "./jobWords";
import { sourceChoices, statusChoices, useJobChoices } from "./useJobChoices";
import { useJobs } from "./useJobs";
import "./jobs.css";

/**
 * The jobs and pipelines (ADR 0027) as every look shows them by default: failing ones first, searched and filtered on
 * the server. The Console look draws its own.
 */
export function JobsScreen() {
  const [params, setParams] = useSearchParams();
  const controls = useJobChoices();
  const { list, error } = useJobs(controls.choices);

  return (
    <main className="jobs">
      <ScreenBar trail={[{ label: "Jobs" }]}>
        <RangePicker
          value={rangeOf(params)}
          onChange={(next) => setParams((current) => withRange(current, next), { replace: true })}
        />
      </ScreenBar>
      <h1>Jobs and pipelines</h1>
      <p className="jobs-lead">
        Jobs recognised from traces, and SQL Server Agent jobs, with their latest run. Failing ones come first.
      </p>
      <div className="jobs-filters">
        <input
          type="search"
          className="jobs-find"
          aria-label="Find a job"
          placeholder="Find a job"
          value={controls.text}
          onChange={(event) => controls.setText(event.target.value)}
        />
        <div className="seg" role="group" aria-label="Status">
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
        <div className="seg" role="group" aria-label="Source">
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
      {error && (
        <p className="jobs-error" role="alert">
          Could not refresh: {error}
        </p>
      )}
      {list === null ? (
        <p>Reading the jobs…</p>
      ) : list.items.length === 0 ? (
        <p>No job matches in the time range.</p>
      ) : (
        <ul className="jobs-list">
          {list.items.map((job) => (
            <li key={job.id} className={job.lastRun?.status === "failed" ? "failed" : job.running ? "running" : ""}>
              <Link to={jobPath(job.id)}>{job.name}</Link>
              <small>
                {sourceWords(job.source)} · {blockWords(job.block)} ·{" "}
                {job.lastRun
                  ? `${statusWords(job.lastRun.status)} ${formatClockTime(job.lastRun.startedAt)}, ${durationWords(job.lastRun.durationSeconds)}`
                  : "no run kept"}{" "}
                · {successWords(job.successRatio)} of {job.runs} runs succeeded
              </small>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
