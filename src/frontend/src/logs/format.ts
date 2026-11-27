import { localDay, shortDate, zoneLabel, zoneParts } from "../time/zone";

/*
 * Formatting shared by every screen. Times are shown in the person's time zone (VEY-22, see time/zone.ts), so a time
 * in the list, the histogram, and the alerts always agree; copied records and exports keep full UTC instants.
 * Numbers follow English conventions, like the rest of the interface.
 */

const counts = new Intl.NumberFormat("en-US");

/**
 * Formats a count with thousands separators, for example `3,118`.
 *
 * @param value - The count.
 * @returns The formatted count.
 */
export function formatCount(value: number): string {
  return counts.format(value);
}

/**
 * The time of day of an instant in the display zone with milliseconds, for example `08:34:12.418`.
 *
 * @param instant - An ISO 8601 instant, as the API sends it.
 * @returns The time of day.
 */
export function formatTime(instant: string): string {
  const parts = zoneParts(instant);

  return `${clock(parts.hour, parts.minute)}:${pad(parts.second)}.${pad(parts.millisecond, 3)}`;
}

/**
 * Hours and minutes of an instant in the display zone, for example `08:34`.
 *
 * @param instant - The instant.
 * @returns The hours and minutes.
 */
export function formatClock(instant: Date | string): string {
  const parts = zoneParts(instant);

  return clock(parts.hour, parts.minute);
}

/**
 * A window in the display zone: its clocks when it stays within one day, as `08:00 – 09:30`, and the day of each end
 * when it spans days, as `Nov 8 13:05 – Nov 9 13:05`, so the last 24 hours never reads as one moment.
 *
 * @param from - The start of the window.
 * @param to - The end of the window.
 * @returns The window in words.
 */
export function formatSpan(from: Date | string, to: Date | string): string {
  if (localDay(from) === localDay(to)) {
    return `${formatClock(from)} – ${formatClock(to)}`;
  }

  return `${shortDate(from)} ${formatClock(from)} – ${shortDate(to)} ${formatClock(to)}`;
}

/**
 * The full instant in the display zone with the zone's name, for example `2026-11-09 08:34:12.418 CET`.
 *
 * @param instant - An ISO 8601 instant.
 * @returns The date and time with the zone.
 */
export function formatInstant(instant: string): string {
  const parts = zoneParts(instant);

  return `${parts.year}-${pad(parts.month)}-${pad(parts.day)} ${formatTime(instant)} ${zoneLabel(new Date(instant))}`;
}

/**
 * The full instant in UTC, for example `2026-11-09 07:34:12.418 UTC`: what hovering a time shows, for comparing
 * with logs elsewhere.
 *
 * @param instant - An ISO 8601 instant.
 * @returns The date and time in UTC.
 */
export function formatUtc(instant: Date | string): string {
  return `${new Date(instant).toISOString().slice(0, 23).replace("T", " ")} UTC`;
}

/**
 * A bucket width in words, for example `2 min`.
 *
 * @param seconds - The width in seconds.
 * @returns The width for a caption.
 */
export function formatBucket(seconds: number): string {
  if (seconds % 86_400 === 0) {
    return seconds === 86_400 ? "day" : `${seconds / 86_400} days`;
  }

  if (seconds % 3_600 === 0) {
    return seconds === 3_600 ? "hour" : `${seconds / 3_600} h`;
  }

  if (seconds % 60 === 0) {
    return seconds === 60 ? "minute" : `${seconds / 60} min`;
  }

  return `${seconds} s`;
}

function clock(hour: number, minute: number): string {
  return `${pad(hour)}:${pad(minute)}`;
}

function pad(value: number, width = 2): string {
  return String(value).padStart(width, "0");
}
