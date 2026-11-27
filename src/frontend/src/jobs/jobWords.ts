import type { AlertBlock, JobListItem, JobRun, JobRunStatus, JobSource, JobStep } from "../api/types";

/*
 * The words and links every look's job screens share (ADR 0027). They only word what the server sent; the server
 * filters, counts, and orders the jobs and their runs.
 */

/** A tone a status is drawn in: green, red, blue while it runs, grey when unknown. */
export type JobTone = "ok" | "bad" | "info" | "off";

/**
 * A run's or a step's status, in words.
 *
 * @param status - The status.
 * @returns `Succeeded`, `Failed`, `Running`, or `Unknown`.
 */
export function statusWords(status: JobRunStatus | JobStep["status"]): string {
  switch (status) {
    case "succeeded":
      return "Succeeded";
    case "failed":
      return "Failed";
    case "running":
      return "Running";
    case "unknown":
      return "Unknown";
  }
}

/**
 * The tone a status is drawn in.
 *
 * @param status - The status.
 * @returns The tone.
 */
export function statusTone(status: JobRunStatus | JobStep["status"]): JobTone {
  switch (status) {
    case "succeeded":
      return "ok";
    case "failed":
      return "bad";
    case "running":
      return "info";
    case "unknown":
      return "off";
  }
}

/**
 * Where a job's runs come from, in words.
 *
 * @param source - The source.
 * @returns `Traces` or `SQL Agent`.
 */
export function sourceWords(source: JobSource): string {
  return source === "trace" ? "Traces" : "SQL Agent";
}

/**
 * A success share as a percentage that never rounds up to 100% while a run failed: `97.9%`, `100%`, or a dash.
 *
 * @param ratio - From 0 to 1, or null without a finished run.
 * @returns The words.
 */
export function successWords(ratio: number | null): string {
  if (ratio === null) {
    return "—";
  }

  // Cut, not rounded: 99.96% is not 100%, which would say nothing failed.
  const tenths = Math.floor(ratio * 1000 + 1e-9) / 10;

  return `${Number(tenths.toFixed(1))}%`;
}

/**
 * The tone of a success share: red below 90%, amber below 99%, green from there.
 *
 * @param ratio - From 0 to 1, or null.
 * @returns The tone, or null without a share.
 */
export function successTone(ratio: number | null): "ok" | "warn" | "bad" | null {
  if (ratio === null) {
    return null;
  }

  return ratio < 0.9 ? "bad" : ratio < 0.99 ? "warn" : "ok";
}

/**
 * How long a run or a step took, as precise as it matters: `240 ms`, `4.2 s`, `38 s`, `6 min 4 s`, `1 h 12 min`.
 *
 * @param seconds - The time, or null.
 * @returns The words, or a dash.
 */
export function durationWords(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }

  if (seconds < 1) {
    return `${Math.round(seconds * 1000)} ms`;
  }

  if (seconds < 10) {
    return `${Number(seconds.toFixed(1))} s`;
  }

  if (seconds < 60) {
    return `${Math.round(seconds)} s`;
  }

  const whole = Math.round(seconds);

  if (whole < 3600) {
    const rest = whole % 60;

    return rest === 0 ? `${Math.floor(whole / 60)} min` : `${Math.floor(whole / 60)} min ${rest} s`;
  }

  const minutes = Math.round(whole / 60);
  const rest = minutes % 60;

  return rest === 0 ? `${Math.floor(minutes / 60)} h` : `${Math.floor(minutes / 60)} h ${rest} min`;
}

/**
 * The records a run processed, against those it expected when it said: `48,200 of 50,000`.
 *
 * @param processed - The records processed, or null.
 * @param total - The records expected, or null.
 * @returns The words, or a dash.
 */
export function recordsWords(processed: number | null, total: number | null): string {
  if (processed === null) {
    return total === null ? "—" : `— of ${total.toLocaleString("en")}`;
  }

  return total === null
    ? processed.toLocaleString("en")
    : `${processed.toLocaleString("en")} of ${total.toLocaleString("en")}`;
}

/**
 * A block's name as people say it: the server without its engine, `sql-prod-01`, or the service.
 *
 * @param block - The block.
 * @returns The name.
 */
export function blockWords(block: AlertBlock): string {
  return block.kind === "database" ? block.name.replace(/^\S+ /, "") : block.name;
}

/**
 * The screen of the block a job runs on: the service's, or the database server's on its Jobs tab.
 *
 * @param block - The block.
 * @returns The address, or null for a kind without a screen.
 */
export function blockPath(block: AlertBlock): string | null {
  if (block.kind === "service") {
    return `/metrics/services/${encodeURIComponent(block.name)}`;
  }

  if (block.kind === "database") {
    const split = block.name.indexOf(" ");

    if (split < 0) {
      return null;
    }

    const engine = block.name.slice(0, split);
    const server = block.name.slice(split + 1);

    return `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}?tab=jobs`;
  }

  return null;
}

/**
 * A job's own screen.
 *
 * @param id - The job's ID.
 * @returns The address.
 */
export function jobPath(id: string): string {
  return `/jobs/${encodeURIComponent(id)}`;
}

/**
 * The Log explorer around one run of a traced job: the service's records from a minute before it started until a
 * minute after it ended, or until now while it runs. SQL Server Agent jobs send no logs of their own, so they have
 * none.
 *
 * @param job - The job.
 * @param run - The run.
 * @param now - The current time, for a run still going on.
 * @returns The address, or null for a job that is not a service's.
 */
export function runLogsPath(job: JobListItem, run: JobRun, now: Date = new Date()): string | null {
  const minute = 60_000;
  const started = Date.parse(run.startedAt);
  const ended = run.durationSeconds === null ? now.getTime() : started + run.durationSeconds * 1000;

  return logsPath(job, new Date(started - minute).toISOString(), new Date(ended + minute).toISOString());
}

/**
 * The Log explorer over a window for the service a traced job runs in, with the service as its query: a name with
 * spaces or other signs is quoted, as the query language reads it.
 *
 * @param job - The job.
 * @param from - The window's start, as ISO.
 * @param to - Its end.
 * @returns The address, or null for a job that is not a service's.
 */
export function logsPath(job: JobListItem, from: string, to: string): string | null {
  if (job.block.kind !== "service") {
    return null;
  }

  const name = job.block.name;
  const service = /^[\w.-]+$/.test(name) ? name : `"${name.replace(/"/g, '\\"')}"`;

  return `/logs?${new URLSearchParams({ q: `service:${service}`, from, to }).toString()}`;
}
