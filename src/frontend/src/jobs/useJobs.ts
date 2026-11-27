import { api } from "../api/client";
import type { JobDetails, JobList, JobSource, JobStatusFilter } from "../api/types";
import { useShell } from "../app/shellContext";
import { useMetricData } from "../metrics/useMetricData";

/** The window outside a shell, as in a component test: the last 24 hours. */
function outsideWindow(): { from: string; to: string } {
  return { from: new Date(Date.now() - 86_400_000).toISOString(), to: new Date().toISOString() };
}

/** The choices the jobs list is asked for with; the server applies every one (ADR 0027). */
export interface JobChoices {
  /** Words the job's name or block must contain; empty for any. */
  q: string;
  /** Which jobs: failing, running, or all. */
  status: JobStatusFilter;
  /** One source only, or null for both. */
  source: JobSource | null;
}

/** What {@link useJobs} gives a screen. */
export interface JobsView {
  /** The latest answer, kept on screen while new choices are asked for; null before the first. */
  list: JobList | null;
  /** Why the latest read failed, or null. */
  error: string | null;
}

/**
 * The jobs of the shell's time range and environment (ADR 0022, ADR 0027), read again on the shell's refresh. Search,
 * status, and source are request parameters: a change asks the server again at once, and the last answer stays on
 * screen until the new one arrives.
 *
 * @param choices - The search, status, and source.
 * @returns The list with its figures, and the latest error.
 */
export function useJobs(choices: JobChoices): JobsView {
  const { status, time } = useShell();
  const environment = status.environment;
  const key = `jobs-${JSON.stringify({ choice: time?.choice ?? null, environment })}`;
  const { data, error } = useMetricData(
    key,
    (signal) => api.jobs({ ...(time ? time.window() : outsideWindow()), environment, ...choices }, signal),
    { choices: JSON.stringify(choices) },
  );

  return { list: data, error };
}

/** What {@link useJob} gives a screen. */
export interface JobView {
  /** The job over the window; null before it arrives. */
  details: JobDetails | null;
  /** Whether the server knows no job by this ID. */
  notFound: boolean;
  /** Why the latest read failed, or null. */
  error: string | null;
}

/**
 * One job over the shell's time range (ADR 0027): its figures, run durations, and runs with their steps, read again on
 * the shell's refresh.
 *
 * @param id - The job's ID.
 * @returns The job, whether it is unknown, and the latest error.
 */
export function useJob(id: string): JobView {
  const { time } = useShell();
  const key = `job-${id}-${JSON.stringify(time?.choice ?? null)}`;
  const { data, notFound, error } = useMetricData(key, (signal) =>
    api.job(id, time ? time.window() : outsideWindow(), signal),
  );

  return { details: data, notFound, error };
}
