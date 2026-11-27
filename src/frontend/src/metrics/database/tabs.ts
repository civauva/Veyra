import { isPostgres } from "./postgres";

/** A tab of the database screen (ADR 0020). */
export interface DatabaseTab {
  /** Its name in the address. */
  id: string;
  /** Its name on screen. */
  label: string;
}

/** The tabs of a SQL Server's Simple view: what an application owner needs, the overview first (VEY-62). */
export const simpleTabs: DatabaseTab[] = [
  { id: "overview", label: "Overview" },
  { id: "live", label: "Live" },
  { id: "queries", label: "Queries" },
  { id: "blocking", label: "Blocking" },
  { id: "databases", label: "Databases" },
  { id: "jobs", label: "Jobs" },
  { id: "events", label: "Events" },
  { id: "server", label: "Server" },
];

/** The tabs of a SQL Server's Advanced view, for a DBA: the Simple view's and the waits. */
export const allTabs: DatabaseTab[] = [
  { id: "overview", label: "Overview" },
  { id: "live", label: "Live" },
  { id: "queries", label: "Queries" },
  { id: "waits", label: "Waits" },
  { id: "blocking", label: "Blocking" },
  { id: "databases", label: "Databases" },
  { id: "jobs", label: "Jobs" },
  { id: "events", label: "Events" },
  { id: "server", label: "Server" },
];

/**
 * The tabs of a PostgreSQL server, in both views (VEY-33): it sends metrics only, so it has none of the tabs that
 * SQL Server's agent fills.
 */
export const postgresTabs: DatabaseTab[] = [
  { id: "overview", label: "Overview" },
  { id: "databases", label: "Databases" },
];

/** The tabs that show nothing over a range, so the range picker leaves the bar on them: they show now. */
const tabsWithoutRange = new Set(["live", "jobs"]);

/**
 * The tabs a server has in a view, before what its platform leaves out.
 *
 * @param engine - As `db.system.name` spells it.
 * @param advanced - Whether the Advanced view is on.
 * @returns The tabs, the default first.
 */
export function tabsOf(engine: string, advanced: boolean): DatabaseTab[] {
  if (isPostgres(engine)) {
    return postgresTabs;
  }

  return advanced ? allTabs : simpleTabs;
}

/**
 * Whether a tab shows its figures over the chosen range, so the bar offers the range on it.
 *
 * @param tab - The tab, by its name in the address.
 * @returns False for the tabs that show now.
 */
export function usesRange(tab: string): boolean {
  return !tabsWithoutRange.has(tab);
}
