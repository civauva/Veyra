import type { AlertQuery, AlertRule, AlertSummary } from "../api/types";
import { formatDuration } from "./alertFormat";
import { ruleOrder } from "./ruleWords";

/*
 * The Alerts list's filters, kept in the address, and its words, shared by every look's Alerts screen (ADR 0021).
 */

/** The periods the screen offers, in days; resolved alerts are kept 90 days by default. */
export const periods = [30, 90] as const;

/** The longest period the alert history answers, in days: resolved alerts are kept 90 days by default. */
const longestDays = 90;

/** How far now may run past a window's start and still count as the same whole days. */
const slackMilliseconds = 5 * 60_000;

/**
 * The days a time window covers, for a look whose top bar chooses the range (ADR 0022): whole days counted back from
 * now to the window's start, at least one and at most the history kept, as the alerts are read by days.
 *
 * @param window - The window, as ISO instants.
 * @param now - The moment counted from; now by default.
 * @returns The days, from 1 to 90.
 */
export function daysOfWindow(window: { from: string; to: string }, now = Date.now()): number {
  // The window is worked out a moment before now is read; a few minutes of slack keep 7 days from reading as 8.
  const days = Math.ceil((now - Date.parse(window.from) - slackMilliseconds) / 86_400_000);

  return Number.isFinite(days) ? Math.min(longestDays, Math.max(1, days)) : 30;
}

/** The sentence under the title. */
export function summarySentence(summary: AlertSummary, days: number): string {
  const open =
    summary.open === 0
      ? "No alert is open."
      : summary.open === 1
        ? "One alert is open."
        : `${summary.open} alerts are open.`;
  const last = days === 1 ? "the last day" : `the last ${days} days`;
  const period =
    summary.total === 0
      ? ` No alerts in ${last}.`
      : ` ${summary.total} ${summary.total === 1 ? "alert" : "alerts"} in ${last}` +
        (summary.medianSeconds === null
          ? "."
          : `; the median cleared in ${formatDuration(summary.medianSeconds * 1000)}.`);

  return open + period;
}

/** How many alerts a state filter holds over the period. */
export function stateCount(summary: AlertSummary, state: AlertQuery["state"]): number {
  return state === "open" ? summary.open : state === "resolved" ? summary.resolved : summary.total;
}

/** The filters from the address; anything unknown falls back to the default. */
export function readQuery(params: URLSearchParams): Omit<AlertQuery, "environment"> {
  const days = Number(params.get("days"));
  const state = params.get("state");

  return {
    days: periods.includes(days as (typeof periods)[number]) ? days : 30,
    state: state === "open" || state === "resolved" ? state : "all",
    rules: params.getAll("rule").filter((rule): rule is AlertRule => (ruleOrder as string[]).includes(rule)),
    text: params.get("q") ?? "",
  };
}

/** The filters as address parameters, leaving out the defaults. */
export function writeQuery(query: Omit<AlertQuery, "environment">): URLSearchParams {
  const params = new URLSearchParams();

  if (query.days !== 30) {
    params.set("days", String(query.days));
  }

  if (query.state !== "all") {
    params.set("state", query.state);
  }

  for (const rule of query.rules) {
    params.append("rule", rule);
  }

  if (query.text) {
    params.set("q", query.text);
  }

  return params;
}
