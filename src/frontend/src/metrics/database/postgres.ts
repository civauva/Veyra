/*
 * What the screens know of a PostgreSQL server (VEY-33, ADR 0018). It sends metrics only, from the Collector's
 * `postgresql` receiver and `sqlquery`: no agent reads it, so it has none of SQL Server's live view, events, or jobs.
 * The colours follow the default levels of its two rules, so a card turns amber where an alert would open.
 */

/** The engine of a PostgreSQL server, as `db.system.name` spells it. */
export const postgresEngine = "postgresql";

/** The connections rule's default levels: caution from 80 % of the maximum, warning from 90 %. */
const connectionLevels = { caution: 0.8, warning: 0.9 };

/** The wraparound rule's default levels: caution from 50 % of the limit, warning from 75 %. */
const wraparoundLevels = { caution: 0.5, warning: 0.75 };

/**
 * Whether a server is PostgreSQL, whose screen has its own tabs.
 *
 * @param engine - As `db.system.name` spells it.
 * @returns True for `postgresql`.
 */
export function isPostgres(engine: string): boolean {
  return engine === postgresEngine;
}

/**
 * The tone of the connections in use of the maximum, at the connections rule's default levels.
 *
 * @param ratio - From 0 to 1, or null when unknown.
 * @returns `ok` below 80 %, `warn` below 90 %, `bad` from 90 %.
 */
export function connectionsTone(ratio: number | null): "ok" | "warn" | "bad" {
  return tone(ratio, connectionLevels);
}

/**
 * The tone of a transaction ID age of the wraparound limit, at the wraparound rule's default levels.
 *
 * @param ratio - From 0 to 1, or null when unknown.
 * @returns `ok` below 50 %, `warn` below 75 %, `bad` from 75 %.
 */
export function wraparoundTone(ratio: number | null): "ok" | "warn" | "bad" {
  return tone(ratio, wraparoundLevels);
}

/**
 * The connections in use of the maximum.
 *
 * @param connections - In use now, or null when unknown.
 * @param maximum - The server's maximum, or null when unknown.
 * @returns From 0 to 1, or null without both.
 */
export function connectionsRatio(connections: number | null, maximum: number | null): number | null {
  return connections === null || maximum === null || maximum <= 0 ? null : connections / maximum;
}

/**
 * A count of rows, short: `48.2M`, `12K`, `950`.
 *
 * @param rows - The rows, or null when unknown.
 * @returns The count, or a dash.
 */
export function formatRows(rows: number | null): string {
  return rows === null ? "–" : compact.format(rows);
}

const compact = new Intl.NumberFormat("en-US", { notation: "compact", maximumFractionDigits: 1 });

function tone(ratio: number | null, levels: { caution: number; warning: number }): "ok" | "warn" | "bad" {
  if (ratio === null || ratio < levels.caution) {
    return "ok";
  }

  return ratio < levels.warning ? "warn" : "bad";
}
