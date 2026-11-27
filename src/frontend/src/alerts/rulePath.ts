import type { AlertRule, BlockKind } from "../api/types";

/** What a warning is about, for the rule page's guidance and its list of open alerts. */
export interface RuleSubject {
  /** A service, host, database server, or container. */
  kind: BlockKind;
  /** Its name; for a database server, its engine and server, as alerts name it. */
  name: string;
  /** The part, such as a disk or a database; null or left out for none. */
  part?: string | null;
}

/**
 * The page that explains a rule and how to fix what it found (VEY-83), for a warning that has no alert of its own to
 * open, such as a late backup a list shows. With a subject, the steps name it and the page lists its open alerts.
 *
 * @param rule - The rule.
 * @param subject - What the warning is about, or nothing for the rule in general.
 * @returns The path, such as `/alerts/rules/db-log-backup-age?kind=database&name=...`.
 */
export function rulePath(rule: AlertRule, subject?: RuleSubject): string {
  const params = new URLSearchParams();

  if (subject) {
    params.set("kind", subject.kind);
    params.set("name", subject.name);

    if (subject.part) {
      params.set("part", subject.part);
    }
  }

  const search = params.toString();

  return `/alerts/rules/${encodeURIComponent(rule)}${search ? `?${search}` : ""}`;
}

/**
 * An alert's page, opened at what it means and how to fix it, which the page scrolls to and marks (VEY-83).
 *
 * @param id - The alert.
 * @returns The path with the fix's anchor.
 */
export function alertFixPath(id: string): string {
  return `/alerts/${encodeURIComponent(id)}#fix`;
}
