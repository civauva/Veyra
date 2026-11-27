import { rangeParams } from "../api/client";
import type { MetricPreset, MetricRange } from "../api/types";
import { zoneParts } from "../time/zone";

/*
 * The shared time range as the metrics screens use it (ADR 0022). It lives in the address, as `range=6h` or as `from`
 * and `to`, and travels through the screens as one string: a preset, or a window written `from~to`.
 */

/** The presets every metrics screen offers, each a window that ends now. */
export const metricRanges: readonly MetricPreset[] = ["15m", "1h", "6h", "24h", "7d", "30d"];

/** The range when the address names none, which the address then leaves out. */
export const defaultRange: MetricPreset = "24h";

/** Each preset in words, for sentences such as "No deadlock in the last 6 hours". */
const presetWords: Record<MetricPreset, string> = {
  "15m": "the last 15 minutes",
  "1h": "the last hour",
  "6h": "the last 6 hours",
  "24h": "the last 24 hours",
  "7d": "the last 7 days",
  "30d": "the last 30 days",
};

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * Whether a range is one of the presets rather than a window of the person's own.
 *
 * @param range - The range.
 * @returns True for a preset.
 */
export function isPreset(range: MetricRange): range is MetricPreset {
  return metricRanges.includes(range as MetricPreset);
}

/** Whether a value is an instant the browser can read. */
function isInstant(value: string | null): value is string {
  return value !== null && !Number.isNaN(Date.parse(value));
}

/**
 * The range an address names: its preset, else its `from` and `to` when they make a window, else the last 24 hours,
 * so an old or edited link never breaks the screen.
 *
 * @param params - The address's query parameters.
 * @returns The range.
 */
export function rangeOf(params: URLSearchParams): MetricRange {
  const range = params.get("range");

  if (range !== null && isPreset(range as MetricRange)) {
    return range as MetricPreset;
  }

  const from = params.get("from");
  const to = params.get("to");

  if (isInstant(from) && isInstant(to) && Date.parse(from) < Date.parse(to)) {
    return `${from}~${to}`;
  }

  return defaultRange;
}

/**
 * The query string that names a range, for links to other screens that keep it.
 *
 * @param range - The range.
 * @returns `range=…`, or `from=…&to=…`.
 */
export function rangeSearch(range: MetricRange): string {
  return new URLSearchParams(rangeParams(range)).toString();
}

/**
 * An address's query parameters with a preset picked: the preset in `range`, or no `range` for the default, and no
 * window of the person's own. Every other parameter stays.
 *
 * @param params - The address's query parameters now.
 * @param preset - The preset picked.
 * @returns The new query parameters.
 */
export function withRange(params: URLSearchParams, preset: MetricPreset): URLSearchParams {
  const next = new URLSearchParams(params);

  next.delete("from");
  next.delete("to");

  if (preset === defaultRange) {
    next.delete("range");
  } else {
    next.set("range", preset);
  }

  return next;
}

/** An instant as a short day and time in the display zone, for example `25 Nov 09:00`. */
function shortMoment(instant: string): string {
  const { month, day, hour, minute } = zoneParts(instant);
  const two = (value: number) => String(value).padStart(2, "0");

  return `${day} ${months[month - 1] ?? ""} ${two(hour)}:${two(minute)}`;
}

/**
 * A range in words, to end a sentence: "the last 6 hours", or "from 25 Nov 09:00 to 26 Nov 09:00" in the zone the
 * person sees times in. A preposition leads only a preset, since a window's words stand on their own: "No deadlock
 * in the last 6 hours", "No deadlock from 25 Nov 09:00 to 26 Nov 09:00".
 *
 * @param range - The range.
 * @param preposition - The word before a preset, such as `over`, `in`, or `within`; none by default.
 * @returns The words.
 */
export function rangeWords(range: MetricRange, preposition?: string): string {
  if (isPreset(range)) {
    return preposition ? `${preposition} ${presetWords[range]}` : presetWords[range];
  }

  const { from, to } = rangeParams(range);

  return `from ${shortMoment(from ?? "")} to ${shortMoment(to ?? "")}`;
}

/**
 * The crumbs before a host's, a service's, or a database server's own name (VEY-83): the look's list it belongs to,
 * such as Console's Hosts & containers, or else Metrics and its list, in the same range.
 *
 * @param parent - Where the look files the screen, or nothing for Metrics.
 * @param range - The range the screen shows.
 * @param label - The name of its list on Metrics.
 * @param anchor - The list's place on Metrics.
 * @returns The crumbs.
 */
export function parentCrumbs(
  parent: { label: string; to: string } | undefined,
  range: MetricRange,
  label: string,
  anchor: string,
): { label: string; to: string }[] {
  if (parent) {
    return [{ label: parent.label, to: `${parent.to}?${rangeSearch(range)}` }];
  }

  return [
    { label: "Metrics", to: `/metrics?${rangeSearch(range)}` },
    { label, to: `/metrics?${rangeSearch(range)}#${anchor}` },
  ];
}
