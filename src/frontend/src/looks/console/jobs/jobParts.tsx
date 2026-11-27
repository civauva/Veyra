import type { JobDuration, JobRunStatus, JobStep } from "../../../api/types";
import { durationWords, statusTone, statusWords, successTone } from "../../../jobs/jobWords";
import { formatClockTime } from "../../../metrics/metricFormat";

/*
 * The small parts Console's job screens share (VEY-69, ADR 0027): a status pill, the strip of the latest runs, and the
 * chart of run durations. They draw what the server sent and work nothing out beyond the scale.
 */

/**
 * The frame of a success figure: red below 90%, amber below 99%, none from there.
 *
 * @param ratio - The success share, or null.
 * @returns The figure's tone, or undefined for none.
 */
export function successFrame(ratio: number | null): "warn" | "bad" | undefined {
  const tone = successTone(ratio);

  return tone === "bad" || tone === "warn" ? tone : undefined;
}

/** Props for {@link StatusPill}. */
export interface StatusPillProps {
  /** The run's or the step's status. */
  status: JobRunStatus | JobStep["status"];
}

/** A run's status as a pill: green, red, blue while it runs, grey when unknown. */
export function StatusPill({ status }: StatusPillProps) {
  return <span className={`cn-pill ${statusTone(status)}`}>{statusWords(status)}</span>;
}

/** Props for {@link RunStrip}. */
export interface RunStripProps {
  /** The statuses of the latest runs, oldest first, as the server sent them. */
  recent: JobRunStatus[];
}

/** The latest runs as a row of small squares, oldest on the left, so a streak of failures shows at a glance. */
export function RunStrip({ recent }: RunStripProps) {
  if (recent.length === 0) {
    return <span className="sq-muted">—</span>;
  }

  const failed = recent.filter((status) => status === "failed").length;
  const last = statusWords(recent.at(-1) ?? "succeeded").toLowerCase();
  const label = `Latest ${recent.length} runs, oldest first: ${failed === 0 ? "none" : failed} failed; the last one ${last}.`;

  return (
    <span className="jb-strip" role="img" aria-label={label}>
      {recent.map((status, index) => (
        <i key={index} className={statusTone(status)} title={statusWords(status)} />
      ))}
    </span>
  );
}

/** Props for {@link DurationBars}. */
export interface DurationBarsProps {
  /** Each run's duration, oldest first. */
  durations: JobDuration[];
  /** The window's start, as ISO. */
  from: string;
  /** Its end. */
  to: string;
  /** The median duration, drawn as a dashed line; null for none. */
  median: number | null;
  /** What the chart sums up as, for screen readers. */
  summary: string;
}

/** The width of the chart's drawing, in its own units; it stretches to its panel. */
const drawWidth = 1000;

/** The height of the chart's drawing. */
const drawHeight = 100;

/**
 * Each run as a bar at the time it started, as high as it took, coloured by how it ended, with the usual duration as a
 * dashed line. A run going on now is a short blue bar. It stretches to its panel's width, so it reads on a phone.
 */
export function DurationBars({ durations, from, to, median, summary }: DurationBarsProps) {
  const start = Date.parse(from);
  const length = Math.max(1, Date.parse(to) - start);
  const longest = Math.max(1e-9, median ?? 0, ...durations.map((run) => run.seconds ?? 0));
  const top = niceSeconds(longest * 1.1);
  const width = Math.max(2, Math.min(18, (drawWidth / Math.max(durations.length, 1)) * 0.62));
  const heightOf = (seconds: number) => Math.max(1.5, (Math.min(seconds, top) / top) * drawHeight);

  return (
    <div className="jb-chart">
      <div className="jb-chart-scale" aria-hidden="true">
        <span>{durationWords(top)}</span>
        <span>{durationWords(top / 2)}</span>
        <span>0</span>
      </div>
      <div className="jb-chart-plot">
        <svg
          viewBox={`0 0 ${drawWidth} ${drawHeight}`}
          preserveAspectRatio="none"
          role="img"
          aria-label={summary}
          className="jb-chart-svg"
        >
          <line className="jb-chart-grid" x1="0" x2={drawWidth} y1={drawHeight / 2} y2={drawHeight / 2} />
          {durations.map((run, index) => {
            const x = ((Date.parse(run.at) - start) / length) * drawWidth;
            const seconds = run.seconds ?? (median ?? top) * 0.25;
            const height = heightOf(seconds);

            return (
              <rect
                key={index}
                className={`jb-bar ${statusTone(run.status)}`}
                x={Math.min(drawWidth - width, Math.max(0, x - width / 2))}
                y={drawHeight - height}
                width={width}
                height={height}
                rx="1"
              >
                <title>
                  {`${formatClockTime(run.at)} · ${statusWords(run.status)}${run.seconds === null ? "" : ` · ${durationWords(run.seconds)}`}`}
                </title>
              </rect>
            );
          })}
          {median !== null && (
            <line
              className="jb-chart-median"
              x1="0"
              x2={drawWidth}
              y1={drawHeight - heightOf(median)}
              y2={drawHeight - heightOf(median)}
            />
          )}
        </svg>
        <div className="jb-chart-times" aria-hidden="true">
          <span>{formatClockTime(from)}</span>
          <span>{formatClockTime(new Date(start + length / 2).toISOString())}</span>
          <span>{formatClockTime(to)}</span>
        </div>
      </div>
      <ul className="jb-legend" aria-hidden="true">
        <li>
          <i className="ok" /> Succeeded
        </li>
        <li>
          <i className="bad" /> Failed
        </li>
        <li>
          <i className="info" /> Running
        </li>
        {median !== null && (
          <li>
            <i className="median" /> Usual {durationWords(median)}
          </li>
        )}
      </ul>
    </div>
  );
}

/**
 * A round top for the duration scale: 1, 2, or 5 times a power of ten seconds, or whole minutes and hours above them.
 *
 * @param seconds - The longest duration to fit.
 * @returns The top of the scale, in seconds.
 */
export function niceSeconds(seconds: number): number {
  const steps =
    seconds <= 60
      ? [1, 2, 5, 10, 15, 20, 30, 45, 60]
      : seconds <= 3600
        ? [90, 120, 180, 300, 600, 900, 1200, 1800, 2700, 3600]
        : [5400, 7200, 10_800, 14_400, 21_600, 43_200, 86_400];

  return steps.find((step) => step >= seconds) ?? Math.ceil(seconds / 86_400) * 86_400;
}
