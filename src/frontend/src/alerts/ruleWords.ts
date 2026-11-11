import type { AlertRule, AlertSeverity, BlockKind } from "../api/types";
import { formatRatio, severityWord } from "../home/homeFormat";
import { formatSeconds } from "../metrics/metricFormat";

/*
 * Words of the built-in alert rules (ADR 0012), shared by the home, the Alerts screen, and the alert details, so an
 * alert reads the same wherever it shows: "payments-gateway logs 21.6% errors", "Disk / on web-01 is full in 9 days".
 * Each rule has its own unit: error rate a ratio, latency seconds, disk space a share, disk filling days until full.
 */

/** The built-in rules in the order every screen lists them. */
export const ruleOrder: AlertRule[] = ["error-rate", "latency-p95", "disk-space", "disk-filling"];

/** The days Veyra records for a disk that is not filling, or not within a year. */
export const notFillingDays = 365;

/** What an alert is about, as the home and the alerts endpoints name it. */
export interface Subject {
  /** A service or a host. */
  kind: BlockKind;
  /** The service or host name. */
  name: string;
  /** The part, for example a disk's mount point; null for none. */
  part: string | null;
}

/**
 * The name of a rule.
 *
 * @param rule - The rule.
 * @returns For example `Error rate` or `Disk filling`.
 */
export function ruleName(rule: AlertRule): string {
  switch (rule) {
    case "error-rate":
      return "Error rate";
    case "latency-p95":
      return "Latency";
    case "disk-space":
      return "Disk space";
    case "disk-filling":
      return "Disk filling";
  }
}

/**
 * Days until a disk is full, the way people say it.
 *
 * @param days - The days; 365 or more means it is not filling.
 * @returns For example `9 days`, `1 day`, `less than a day`, or `not filling`.
 */
export function formatDays(days: number): string {
  if (days >= notFillingDays) {
    return "not filling";
  }

  if (days < 0.5) {
    return "less than a day";
  }

  const whole = Math.round(days);

  return whole === 1 ? "1 day" : `${whole} days`;
}

/**
 * The share of a disk in use, in whole percent; one decimal above 99 % so a full disk does not read 100 % too early.
 *
 * @param ratio - From 0 to 1.
 * @returns For example `86%` or `99.6%`.
 */
export function formatDiskShare(ratio: number): string {
  const percent = ratio * 100;

  return `${percent > 99 && percent < 100 ? Number(percent.toFixed(1)) : Math.round(percent)}%`;
}

/**
 * A measurement in its rule's unit.
 *
 * @param rule - The rule.
 * @param value - The measurement.
 * @returns For example `6.2%`, `420 ms`, `91%`, or `9 days`.
 */
export function formatMeasure(rule: AlertRule, value: number): string {
  switch (rule) {
    case "latency-p95":
      return formatSeconds(value);
    case "disk-space":
      return formatDiskShare(value);
    case "disk-filling":
      return formatDays(value);
    case "error-rate":
      return formatRatio(value);
  }
}

/**
 * The name of what an alert is about, short enough for a row.
 *
 * @param subject - The subject.
 * @returns For example `payments-gateway` or `web-01 · /var/log`.
 */
export function subjectLabel(subject: Subject): string {
  return subject.part ? `${subject.name} · ${subject.part}` : subject.name;
}

/**
 * The title of an alert: what is wrong now, or, once resolved, what was wrong at its worst.
 *
 * @param rule - The rule.
 * @param subject - What it is about.
 * @param value - The measurement to name: the latest while open, the peak once resolved.
 * @param resolved - Whether the alert resolved.
 * @returns For example `Disk / on web-01 is full in 9 days` or `checkout-web answered slowly: p95 3.4 s`.
 */
export function alertTitle(rule: AlertRule, subject: Subject, value: number, resolved: boolean): string {
  const disk = `Disk ${subject.part ?? ""} on ${subject.name}`;

  switch (rule) {
    case "latency-p95":
      return `${subject.name} ${resolved ? "answered" : "answers"} slowly: p95 ${formatSeconds(value)}`;
    case "disk-space":
      return `${disk} ${resolved ? "was" : "is"} ${formatDiskShare(value)} full`;
    case "disk-filling":
      return resolved ? `${disk} was filling: full in ${formatDays(value)}` : `${disk} is full in ${formatDays(value)}`;
    case "error-rate":
      return `${subject.name} ${resolved ? "logged" : "logs"} ${formatRatio(value)} errors`;
  }
}

/**
 * The rule as one short line, for a row under the alert's name.
 *
 * @param rule - The rule.
 * @param threshold - The threshold of the alert's severity.
 * @param windowSeconds - The rule's window.
 * @returns For example `Error rate · above 5% for 5 min` or `Disk filling · full within 14 days`.
 */
export function ruleSummary(rule: AlertRule, threshold: number, windowSeconds: number): string {
  const minutes = Math.round(windowSeconds / 60);

  switch (rule) {
    case "latency-p95":
      return `Latency · p95 above ${formatSeconds(threshold)} for ${minutes} min`;
    case "disk-space":
      return `Disk space · above ${formatDiskShare(threshold)} in use`;
    case "disk-filling":
      return `Disk filling · full within ${formatDays(threshold)}`;
    case "error-rate":
      return `Error rate · above ${formatRatio(threshold)} for ${minutes} min`;
  }
}

/**
 * The rule behind an alert in one sentence.
 *
 * @param rule - The rule.
 * @param severity - The alert's severity.
 * @param threshold - The threshold of that severity.
 * @param windowSeconds - The rule's window.
 * @returns The sentence.
 */
export function ruleSentence(
  rule: AlertRule,
  severity: AlertSeverity,
  threshold: number,
  windowSeconds: number,
): string {
  const word = severityWord(severity);
  const minutes = Math.round(windowSeconds / 60);

  switch (rule) {
    case "latency-p95":
      return `Built-in rule: ${word} when the p95 of requests is ${formatSeconds(threshold)} or more over ${minutes} minutes.`;
    case "disk-space":
      return `Built-in rule: ${word} when ${formatDiskShare(threshold)} or more of a disk is in use.`;
    case "disk-filling":
      return `Built-in rule: ${word} when a disk is full within ${formatDays(threshold)} at the pace of the last day.`;
    case "error-rate":
      return `Built-in rule: ${word} when ${formatRatio(threshold)} or more of records are errors over ${minutes} minutes.`;
  }
}

/**
 * What a rule measures and its two thresholds, for the list of built-in rules.
 *
 * @param rule - The rule.
 * @param windowSeconds - Its window.
 * @returns What it measures, before the thresholds.
 */
export function ruleMeasures(rule: AlertRule, windowSeconds: number): string {
  const minutes = Math.round(windowSeconds / 60);

  switch (rule) {
    case "latency-p95":
      return `p95 of a service's requests over ${minutes} minutes`;
    case "disk-space":
      return "Share of a disk in use";
    case "disk-filling":
      return "Full at the pace of the last day";
    case "error-rate":
      return `Of a service's log records over ${minutes} minutes`;
  }
}

/**
 * A threshold as the list of built-in rules writes it.
 *
 * @param rule - The rule.
 * @param threshold - The threshold.
 * @returns For example `1%`, `1 s`, `90%`, or `within 14 days`.
 */
export function formatThreshold(rule: AlertRule, threshold: number): string {
  return rule === "disk-filling" ? `within ${formatDays(threshold)}` : formatMeasure(rule, threshold);
}
