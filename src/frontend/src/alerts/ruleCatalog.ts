import { api } from "../api/client";
import type { AlertRule, AlertSeverity, CatalogRule, CatalogUnit } from "../api/types";
import { formatRatio, severityWord } from "../home/homeFormat";
import { formatAmount, formatSpan } from "../metrics/metricFormat";

/*
 * Words of the catalog rules (ADR 0020). Where the rules of ADR 0012 and ADR 0018 each have their own sentences in
 * ruleWords, a catalog rule words itself from one entry the API sends: its name, unit, and headline. A new catalog rule
 * then needs no words written here, only its name in the AlertRule type.
 */

/** The catalog rules the screens know by name. */
export const catalogRuleIds = [
  "db-head-blocker",
  "db-open-transaction",
  "db-tempdb-session",
  "db-plan-regression",
  "db-offline",
  "db-log-full",
  "db-vlf-count",
  "db-volume-space",
  "db-log-backup-age",
  "db-log-chain",
  "db-checkdb-age",
  "db-damaged-pages",
  "db-cpu",
  "db-cpu-other",
  "db-workers",
  "db-grants-pending",
  "db-os-memory-low",
  "db-memory-dump",
  "db-agent-stopped",
  "db-restart",
  "db-unsupported",
  "db-job-overrun",
  "db-job-late",
  "db-severe-error",
  "db-memory-error",
  "db-login-failures",
  "db-ag-unhealthy",
  "db-ag-queue",
  "db-ag-sync",
  "db-ag-failover",
  "db-logshipping",
] as const;

/** The name of a catalog rule. */
export type CatalogRuleId = (typeof catalogRuleIds)[number];

/** What a subject of an alert is, as the catalog's words need it. */
interface CatalogSubject {
  /** The block's name: for a database server, its engine and server. */
  name: string;
  /** The part of the block, or null. */
  part: string | null;
}

// What the screens fall back to until the API has answered: each rule's name and unit, so values read right.
const fallback: Record<CatalogRuleId, { name: string; unit: CatalogUnit }> = {
  "db-head-blocker": { name: "Head blocker", unit: "seconds" },
  "db-open-transaction": { name: "Open transaction", unit: "seconds" },
  "db-tempdb-session": { name: "Session filling tempdb", unit: "ratio" },
  "db-plan-regression": { name: "Plan regression", unit: "count" },
  "db-offline": { name: "Database not online", unit: "flag" },
  "db-log-full": { name: "Log filling", unit: "ratio" },
  "db-vlf-count": { name: "Virtual log files", unit: "count" },
  "db-volume-space": { name: "Database volume space", unit: "ratio" },
  "db-log-backup-age": { name: "Log backup age", unit: "seconds" },
  "db-log-chain": { name: "Broken log chain", unit: "flag" },
  "db-checkdb-age": { name: "Integrity check age", unit: "seconds" },
  "db-damaged-pages": { name: "Damaged pages", unit: "count" },
  "db-cpu": { name: "SQL Server CPU", unit: "ratio" },
  "db-cpu-other": { name: "CPU taken by others", unit: "ratio" },
  "db-workers": { name: "Worker threads", unit: "ratio" },
  "db-grants-pending": { name: "Memory grants waiting", unit: "count" },
  "db-os-memory-low": { name: "Machine low on memory", unit: "flag" },
  "db-memory-dump": { name: "Memory dump", unit: "count" },
  "db-agent-stopped": { name: "SQL Server Agent stopped", unit: "flag" },
  "db-restart": { name: "Restart", unit: "flag" },
  "db-unsupported": { name: "Version out of support", unit: "flag" },
  "db-job-overrun": { name: "Job running long", unit: "count" },
  "db-job-late": { name: "Job did not run", unit: "flag" },
  "db-severe-error": { name: "Severe error", unit: "count" },
  "db-memory-error": { name: "Memory error", unit: "count" },
  "db-login-failures": { name: "Failed logins", unit: "count" },
  "db-ag-unhealthy": { name: "Replica not healthy", unit: "flag" },
  "db-ag-queue": { name: "Replica falling behind", unit: "bytes" },
  "db-ag-sync": { name: "Database not synchronizing", unit: "flag" },
  "db-ag-failover": { name: "Failover", unit: "flag" },
  "db-logshipping": { name: "Log shipping behind", unit: "flag" },
};

let catalog = new Map<string, CatalogRule>();
let loading: Promise<void> | null = null;

/**
 * Whether a rule is a catalog rule, so a caller words it from the catalog.
 *
 * @param rule - The rule.
 * @returns True for a catalog rule.
 */
export function isCatalogRule(rule: AlertRule): rule is CatalogRuleId {
  return (catalogRuleIds as readonly string[]).includes(rule);
}

/**
 * Loads the catalog once, quietly: until it answers, and if it fails, the screens use each rule's name and unit.
 *
 * @returns A promise that settles when the catalog is loaded or failed.
 */
export function loadRuleCatalog(): Promise<void> {
  // Started inside a promise, so even a call that throws at once leaves the screens on the plain words.
  loading ??= Promise.resolve()
    .then(() => api.alertCatalog())
    .then((rules) => setRuleCatalog(rules))
    .catch(() => {
      loading = null;
    });

  return loading;
}

/**
 * Sets the catalog, as the API sent it; tests set it directly.
 *
 * @param rules - The catalog rules.
 */
export function setRuleCatalog(rules: CatalogRule[]): void {
  catalog = new Map(rules.map((rule) => [rule.id, rule]));
}

/**
 * A catalog rule's name.
 *
 * @param rule - The rule.
 * @returns For example `Head blocker`.
 */
export function catalogName(rule: CatalogRuleId): string {
  return catalog.get(rule)?.name ?? fallback[rule].name;
}

/**
 * A measurement in a catalog rule's unit.
 *
 * @param rule - The rule.
 * @param value - The measurement.
 * @returns For example `3 min`, `42%`, or `3 requests`.
 */
export function catalogValue(rule: CatalogRuleId, value: number): string {
  const entry = catalog.get(rule);

  switch (entry?.unit ?? fallback[rule].unit) {
    case "seconds":
      return formatSpan(value);
    case "ratio":
      return formatRatio(value);
    case "count": {
      const whole = Math.round(value);
      const noun = entry?.noun ?? "item";

      return whole === 1 ? `1 ${noun}` : `${whole.toLocaleString("en-US")} ${noun}s`;
    }
    case "bytes":
      return formatAmount(value);
    case "days":
      return Math.round(value) === 1 ? "1 day" : `${Math.round(value)} days`;
    case "flag":
      return value >= 1 ? "yes" : "no";
  }
}

/**
 * The title of a catalog rule's alert, from its headline, or its recovery once resolved.
 *
 * @param rule - The rule.
 * @param subject - What the alert is about.
 * @param server - The server the way people say it, such as `SQL Server sql-prod-01`.
 * @param value - The measurement.
 * @param resolved - Whether the alert resolved.
 * @returns For example `SQL Server sql-prod-01: requests have waited 3 min behind another session`.
 */
export function catalogTitle(
  rule: CatalogRuleId,
  subject: CatalogSubject,
  server: string,
  value: number,
  resolved: boolean,
): string {
  const entry = catalog.get(rule);
  const template = entry ? (resolved ? entry.recovery : entry.headline) : `{server}: ${catalogName(rule)} {value}`;

  return template
    .replaceAll("{server}", server)
    .replaceAll("{part}", subject.part ?? "")
    .replaceAll("{value}", catalogValue(rule, value));
}

/**
 * A catalog rule as one short line.
 *
 * @param rule - The rule.
 * @param threshold - The threshold of the alert's severity.
 * @returns For example `Head blocker · 1 min or more`.
 */
export function catalogSummary(rule: CatalogRuleId, threshold: number): string {
  const direction = catalog.get(rule)?.lowerIsWorse ? "or less" : "or more";

  return `${catalogName(rule)} · ${catalogValue(rule, threshold)} ${direction}`;
}

/**
 * A catalog rule in one sentence.
 *
 * @param rule - The rule.
 * @param severity - The alert's severity.
 * @param threshold - The threshold of that severity.
 * @returns The sentence.
 */
export function catalogSentence(rule: CatalogRuleId, severity: AlertSeverity, threshold: number): string {
  const entry = catalog.get(rule);
  const direction = entry?.lowerIsWorse ? "at or below" : "at";
  const what = entry?.description ? ` ${entry.description}` : "";

  return `Built-in rule: ${severityWord(severity)} ${direction} ${catalogValue(rule, threshold)}.${what}`;
}

/**
 * What a catalog rule measures, for the list of built-in rules.
 *
 * @param rule - The rule.
 * @returns Its description, or its name until the catalog is loaded.
 */
export function catalogMeasures(rule: CatalogRuleId): string {
  return catalog.get(rule)?.description ?? catalogName(rule);
}
