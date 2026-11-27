import { offsetMinutes, zoneParts } from "../time/zone";

/**
 * The one time range every screen shows (ADR 0022). It lives in the address, as `range=24h` or as `from` and `to`, so
 * a shared link shows the same window; the shell carries it from screen to screen.
 */

/** The ranges a person can pick, each a window that ends now. */
export const timePresets = ["15m", "1h", "6h", "24h", "7d", "30d"] as const;

/** One of the ranges ending now. */
export type TimePreset = (typeof timePresets)[number];

/** The range a person picked: a preset ending now, or a window of their own. */
export interface TimeChoice {
  /** The preset, or null for a window of their own. */
  preset: TimePreset | null;
  /** The window's start, as an ISO instant, for a window of their own; null for a preset. */
  from: string | null;
  /** The window's end, as an ISO instant, for a window of their own; null for a preset. */
  to: string | null;
}

/** A window of time, as ISO instants. */
export interface TimeWindow {
  /** Its start. */
  from: string;
  /** Its end. */
  to: string;
}

/** The range when the address names none: the last 24 hours, as the reference design opens. */
export const defaultTime: TimeChoice = { preset: "24h", from: null, to: null };

/** Each preset's length in milliseconds. */
const lengths: Record<TimePreset, number> = {
  "15m": 15 * 60_000,
  "1h": 3_600_000,
  "6h": 6 * 3_600_000,
  "24h": 24 * 3_600_000,
  "7d": 7 * 24 * 3_600_000,
  "30d": 30 * 24 * 3_600_000,
};

/** Each preset in words, for menus and labels. */
export const presetNames: Record<TimePreset, string> = {
  "15m": "Last 15 minutes",
  "1h": "Last hour",
  "6h": "Last 6 hours",
  "24h": "Last 24 hours",
  "7d": "Last 7 days",
  "30d": "Last 30 days",
};

/** Whether a value is an instant the browser can read. */
function isInstant(value: string | null): value is string {
  return value !== null && !Number.isNaN(Date.parse(value));
}

/**
 * The range an address names. Anything unknown, or a window that ends before it starts, falls back to the last 24
 * hours, so an old or edited link never breaks the page.
 *
 * @param params - The address's query parameters.
 * @returns The range.
 */
export function timeFrom(params: URLSearchParams): TimeChoice {
  const range = params.get("range");

  if (timePresets.includes(range as TimePreset)) {
    return { preset: range as TimePreset, from: null, to: null };
  }

  const from = params.get("from");
  const to = params.get("to");

  if (isInstant(from) && isInstant(to) && Date.parse(from) < Date.parse(to)) {
    return { preset: null, from, to };
  }

  return defaultTime;
}

/**
 * The window a range covers at an instant.
 *
 * @param time - The range.
 * @param now - The instant, in milliseconds since the epoch.
 * @returns The window, as ISO instants.
 */
export function windowOf(time: TimeChoice, now: number): TimeWindow {
  if (time.preset === null && time.from !== null && time.to !== null) {
    return { from: time.from, to: time.to };
  }

  const length = lengths[time.preset ?? "24h"];

  return { from: new Date(now - length).toISOString(), to: new Date(now).toISOString() };
}

/**
 * The query string that names a range.
 *
 * @param time - The range.
 * @returns `range=…`, or `from=…&to=…`.
 */
export function timeSearch(time: TimeChoice): string {
  const params = new URLSearchParams();

  if (time.preset !== null) {
    params.set("range", time.preset);
  } else if (time.from !== null && time.to !== null) {
    params.set("from", time.from);
    params.set("to", time.to);
  }

  return params.toString();
}

/** Two digits, as a wall time writes them. */
const two = (value: number) => String(value).padStart(2, "0");

/**
 * The wall time an instant shows in a zone, as a date and time field holds it.
 *
 * @param instant - The instant, as ISO.
 * @param zone - The IANA zone the person sees times in.
 * @returns `YYYY-MM-DDTHH:mm`.
 */
export function wallOf(instant: string, zone: string): string {
  const parts = zoneParts(instant, zone);

  return `${parts.year}-${two(parts.month)}-${two(parts.day)}T${two(parts.hour)}:${two(parts.minute)}`;
}

/**
 * The instant a wall time stands for in a zone, as a custom range's From and To are typed.
 *
 * @param wall - `YYYY-MM-DDTHH:mm`, as a date and time field gives it.
 * @param zone - The IANA zone the person sees times in.
 * @returns The instant as ISO, or null when the field holds no time.
 */
export function instantOfWall(wall: string, zone: string): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(wall);

  if (!match) {
    return null;
  }

  const [year, month, day, hour, minute] = match.slice(1).map(Number) as [number, number, number, number, number];
  const asUtc = Date.UTC(year, month - 1, day, hour, minute);
  // The zone's offset at that moment; asked twice so a wall time next to a clock change lands right.
  const first = asUtc - offsetMinutes(asUtc, zone) * 60_000;

  return new Date(asUtc - offsetMinutes(first, zone) * 60_000).toISOString();
}
