import type { AlertRule, AlertSeverity, BlockKind } from "../api/types";
import { formatRatio, severityWord } from "../home/homeFormat";
import { formatSeconds, formatSpan } from "../metrics/metricFormat";
import {
  catalogMeasures,
  catalogName,
  catalogRuleIds,
  catalogSentence,
  catalogSummary,
  catalogTitle,
  catalogValue,
  isCatalogRule,
} from "./ruleCatalog";

/*
 * Words of the built-in alert rules (ADR 0012), shared by the home, the Alerts screen, and the alert details, so an
 * alert reads the same wherever it shows: "payments-gateway logs 21.6% errors", "Disk / on web-01 is full in 9 days".
 * Each rule has its own unit: error rate a ratio, latency seconds, disk space a share, disk filling days until full,
 * and the database rules sessions, deadlocks, seconds, or a failed run (ADR 0018).
 */

/** The built-in rules in the order every screen lists them. */
export const ruleOrder: AlertRule[] = [
  "error-rate",
  "latency-p95",
  "disk-space",
  "disk-filling",
  "db-blocking",
  "db-deadlocks",
  "db-long-query",
  "db-backup-age",
  "db-agent-job",
  ...catalogRuleIds,
];

/**
 * A database server the way people say it.
 *
 * @param block - The server's block name, its engine and server, such as `microsoft.sql_server sql-prod-01`.
 * @returns For example `SQL Server sql-prod-01`.
 */
export function serverName(block: string): string {
  const [engine = "", ...server] = block.split(" ");

  return [engineName(engine), ...server].join(" ");
}

/**
 * The path of a database server's screen.
 *
 * @param block - The server's block name, its engine and server, such as `microsoft.sql_server sql-prod-01`.
 * @returns For example `/metrics/databases/microsoft.sql_server/sql-prod-01`.
 */
export function databasePath(block: string): string {
  const [engine = "", ...server] = block.split(" ");

  return `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server.join(" "))}`;
}

/**
 * An engine the way people say it.
 *
 * @param engine - As `db.system.name` spells it, such as `microsoft.sql_server`.
 * @returns For example `SQL Server` or `PostgreSQL`.
 */
export function engineName(engine: string): string {
  switch (engine) {
    case "microsoft.sql_server":
      return "SQL Server";
    case "postgresql":
      return "PostgreSQL";
    default:
      return engine;
  }
}

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
  if (isCatalogRule(rule)) {
    return catalogName(rule);
  }

  switch (rule) {
    case "error-rate":
      return "Error rate";
    case "latency-p95":
      return "Latency";
    case "disk-space":
      return "Disk space";
    case "disk-filling":
      return "Disk filling";
    case "db-blocking":
      return "Blocking";
    case "db-deadlocks":
      return "Deadlocks";
    case "db-long-query":
      return "Long query";
    case "db-backup-age":
      return "Backup age";
    case "db-agent-job":
      return "SQL Agent job";
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
  if (isCatalogRule(rule)) {
    return catalogValue(rule, value);
  }

  switch (rule) {
    case "latency-p95":
      return formatSeconds(value);
    case "disk-space":
      return formatDiskShare(value);
    case "disk-filling":
      return formatDays(value);
    case "error-rate":
      return formatRatio(value);
    case "db-blocking":
      return count(value, "session");
    case "db-deadlocks":
      return count(value, "deadlock");
    case "db-long-query":
    case "db-backup-age":
      return formatSpan(value);
    case "db-agent-job":
      return value >= 1 ? "failed" : "succeeded";
  }
}

/**
 * The name of what an alert is about, short enough for a row.
 *
 * @param subject - The subject.
 * @returns For example `payments-gateway`, `web-01 · /var/log`, or `SQL Server sql-prod-01 · shop`.
 */
export function subjectLabel(subject: Subject): string {
  const name = subject.kind === "database" ? serverName(subject.name) : subject.name;

  return subject.part ? `${name} · ${subject.part}` : name;
}

// "1 session", "3 sessions".
function count(value: number, noun: string): string {
  const whole = Math.round(value);

  return whole === 1 ? `1 ${noun}` : `${whole} ${noun}s`;
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
  const server = serverName(subject.name);

  if (isCatalogRule(rule)) {
    return catalogTitle(rule, subject, server, value, resolved);
  }

  switch (rule) {
    case "latency-p95":
      return `${subject.name} ${resolved ? "answered" : "answers"} slowly: p95 ${formatSeconds(value)}`;
    case "disk-space":
      return `${disk} ${resolved ? "was" : "is"} ${formatDiskShare(value)} full`;
    case "disk-filling":
      return resolved ? `${disk} was filling: full in ${formatDays(value)}` : `${disk} is full in ${formatDays(value)}`;
    case "error-rate":
      return `${subject.name} ${resolved ? "logged" : "logs"} ${formatRatio(value)} errors`;
    case "db-blocking":
      return `${server}: ${count(value, "session")} ${resolved ? "were" : "are"} blocked`;
    case "db-deadlocks":
      return `${server}: ${count(value, "deadlock")} in 15 min`;
    case "db-long-query":
      return `${server}: a query in ${subject.part ?? "a database"} ${resolved ? "ran" : "has run"} for ${formatSpan(value)}`;
    case "db-backup-age":
      return `${server}: ${subject.part ?? "a database"} ${resolved ? "had" : "has"} no full backup for ${formatSpan(value)}`;
    case "db-agent-job":
      return `${server}: SQL Agent job ${subject.part ?? ""} ${resolved ? "had failed" : "failed"}`;
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
  if (isCatalogRule(rule)) {
    return catalogSummary(rule, threshold);
  }

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
    case "db-blocking":
      return `Blocking · ${count(threshold, "session")} or more for ${minutes} min`;
    case "db-deadlocks":
      return `Deadlocks · ${count(threshold, "deadlock")} or more in ${minutes} min`;
    case "db-long-query":
      return `Long query · running for ${formatSpan(threshold)} or more`;
    case "db-backup-age":
      return `Backup age · no full backup for ${formatSpan(threshold)}`;
    case "db-agent-job":
      return "SQL Agent job · the last run failed";
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
  if (isCatalogRule(rule)) {
    return catalogSentence(rule, severity, threshold);
  }

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
    case "db-blocking":
      return `Built-in rule: ${word} when ${count(threshold, "session")} or more stay blocked for ${minutes} minutes. It clears after 5 minutes without one.`;
    case "db-deadlocks":
      return `Built-in rule: ${word} at ${count(threshold, "deadlock")} or more in ${minutes} minutes. It clears after 30 minutes without one.`;
    case "db-long-query":
      return `Built-in rule: ${word} when a query has run for ${formatSpan(threshold)} or more. It clears once none has for 5 minutes.`;
    case "db-backup-age":
      return `Built-in rule: ${word} when a database has had no full backup for ${formatSpan(threshold)}. A new backup clears it.`;
    case "db-agent-job":
      return `Built-in rule: ${word} when the last run of a SQL Agent job failed. The next successful run clears it.`;
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
  if (isCatalogRule(rule)) {
    return catalogMeasures(rule);
  }

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
    case "db-blocking":
      return `Sessions of a database server blocked for ${minutes} minutes`;
    case "db-deadlocks":
      return `Deadlocks on a database server in ${minutes} minutes`;
    case "db-long-query":
      return "The longest running query of a database";
    case "db-backup-age":
      return "Time since a database's last full backup";
    case "db-agent-job":
      return "The last run of a SQL Agent job";
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
