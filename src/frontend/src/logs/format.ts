/*
 * Formatting shared by the Log Explorer. Times are shown in UTC everywhere, so a time in the list, the histogram, and
 * a copied record always agree, whatever the browser's time zone. Numbers follow English conventions, like the rest
 * of the interface.
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
 * The time of day of an instant in UTC with milliseconds, for example `07:34:12.418`.
 *
 * @param instant - An ISO 8601 instant, as the API sends it.
 * @returns The time of day.
 */
export function formatTime(instant: string): string {
  return new Date(instant).toISOString().slice(11, 23);
}

/**
 * Hours and minutes of an instant in UTC, for example `07:34`.
 *
 * @param instant - The instant.
 * @returns The hours and minutes.
 */
export function formatClock(instant: Date | string): string {
  return new Date(instant).toISOString().slice(11, 16);
}

/**
 * The full instant in UTC, for example `2026-11-09 07:34:12.418 UTC`.
 *
 * @param instant - An ISO 8601 instant.
 * @returns The date and time with the zone.
 */
export function formatInstant(instant: string): string {
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
