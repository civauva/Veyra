import { formatBytes } from "../home/homeFormat";

/*
 * Numbers of the metrics screens and the home's metric tiles. Shares are percentages without decimals above 10 %,
 * durations switch from milliseconds to seconds at one second, and rates keep two significant digits.
 */

/**
 * A duration in the unit people read it in.
 *
 * @param seconds - The duration in seconds, or null when unknown.
 * @returns For example `340 ms`, `1.2 s`, or `—`.
 */
export function formatSeconds(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }

  if (seconds < 1) {
    const milliseconds = seconds * 1000;

    return `${milliseconds < 10 ? Number(milliseconds.toFixed(1)) : Math.round(milliseconds)} ms`;
  }

  return `${Number(seconds.toFixed(seconds < 10 ? 1 : 0))} s`;
}

/**
 * A share as a whole percentage, with one decimal below 10 %.
 *
 * @param share - From 0 to 1, or null when unknown.
 * @returns For example `42%`, `2.1%`, `<0.1%`, or `—`.
 */
export function formatShare(share: number | null): string {
  if (share === null) {
    return "—";
  }

  const percent = share * 100;

  // An idle process still uses some CPU; "0%" would read as stopped.
  if (percent > 0 && percent < 0.05) {
    return "<0.1%";
  }

  return `${percent < 10 ? Number(percent.toFixed(1)) : Math.round(percent)}%`;
}

/**
 * A rate with two significant digits and thousands separators above 100.
 *
 * @param rate - The rate, or null when unknown.
 * @returns For example `118`, `2.5`, `0.04`, or `—`.
 */
export function formatRate(rate: number | null): string {
  if (rate === null) {
    return "—";
  }

  if (rate >= 100) {
    return Math.round(rate).toLocaleString("en-US");
  }

  return String(Number(rate.toPrecision(2)));
}

/**
 * Bytes, or bytes per second when `perSecond` is set.
 *
 * @param bytes - The amount, or null when unknown.
 * @param perSecond - Whether it is a rate.
 * @returns For example `1.2 GB` or `14 MB/s`.
 */
export function formatAmount(bytes: number | null, perSecond = false): string {
  if (bytes === null) {
    return "—";
  }

  return `${formatBytes(bytes)}${perSecond ? "/s" : ""}`;
}

/**
 * How long ago something happened, in seconds, minutes, or hours.
 *
 * @param instant - When it happened.
 * @param now - The current time.
 * @returns For example `12 s ago`, `4 min ago`, or `2 h ago`.
 */
export function formatAgo(instant: string, now: Date): string {
  const seconds = Math.max(0, Math.round((now.getTime() - new Date(instant).getTime()) / 1000));

  if (seconds < 60) {
    return `${seconds} s ago`;
  }

  const minutes = Math.round(seconds / 60);

  return minutes < 60 ? `${minutes} min ago` : `${Math.round(minutes / 60)} h ago`;
}

/**
 * The tone of a share in use, as the rings and bars colour it.
 *
 * @param share - From 0 to 1.
 * @returns `ok` below 75 %, `warn` below 90 %, `bad` from 90 %.
 */
export function shareTone(share: number | null): "ok" | "warn" | "bad" {
  if (share === null || share < 0.75) {
    return "ok";
  }

  return share < 0.9 ? "warn" : "bad";
}

/**
 * When a disk is full, in words.
 *
 * @param days - Days until full, or null when it is not filling.
 * @returns For example `full in about 23 days`, `full within a day`, or `steady`.
 */
export function formatFull(days: number | null): string {
  if (days === null) {
    return "steady";
  }

  if (days < 1) {
    return "full within a day";
  }

  return `full in about ${Math.round(days)} ${Math.round(days) === 1 ? "day" : "days"}`;
}
