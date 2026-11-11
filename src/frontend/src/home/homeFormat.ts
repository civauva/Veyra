import type { AlertSeverity, ConditionKind } from "../api/types";

/*
 * Words and numbers of the home dashboards. Times are UTC, like everywhere in Veyra, so the home and the Log
 * Explorer always show the same clock. Severities use the shared words: warning (red) and caution (amber).
 */

/**
 * Formats an error ratio as a percentage: two decimals below 0.1 %, otherwise one, without trailing zeros.
 *
 * @param ratio - From 0 to 1, or null when there were no records.
 * @returns For example `5.8%`, `0.02%`, or `—`.
 */
export function formatRatio(ratio: number | null): string {
  if (ratio === null) {
    return "—";
  }

  const percent = ratio * 100;
  const rounded = Number(percent.toFixed(percent < 0.1 ? 2 : 1));

  return `${rounded}%`;
}

/**
 * The word for a severity, the same on every screen.
 *
 * @param severity - The severity.
 * @returns `warning` or `caution`.
 */
export function severityWord(severity: AlertSeverity): string {
  return severity === "Warning" ? "warning" : "caution";
}

/**
 * The headline of the Forecast home.
 *
 * @param kind - The weather.
 * @param over - The group that holds alert 1.
 * @returns For example `Storm over Services` or `Clear skies`.
 */
export function conditionTitle(kind: ConditionKind, over: string | null): string {
  switch (kind) {
    case "Storm":
      return `Storm over ${over ?? "Services"}`;
    case "Showers":
      return `Showers over ${over ?? "Services"}`;
    case "Clear":
      return "Clear skies";
  }
}

/**
 * How long ago something started, coarsely: minutes, then hours, then days.
 *
 * @param since - An ISO 8601 instant.
 * @param now - The current time.
 * @returns For example `36 min`, `2 h`, or `4 d`.
 */
export function formatAge(since: string, now: Date): string {
  const minutes = (now.getTime() - new Date(since).getTime()) / 60_000;

  if (minutes < 60) {
    return `${Math.max(1, Math.floor(minutes))} min`;
  }

  if (minutes < 48 * 60) {
    return `${Math.floor(minutes / 60)} h`;
  }

  return `${Math.floor(minutes / (24 * 60))} d`;
}

/**
 * A size in bytes with a readable unit, in powers of 1024 like the storage budget.
 *
 * @param bytes - The size.
 * @returns For example `340 MB` or `1.2 GB`.
 */
export function formatBytes(bytes: number): string {
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }

  const shown = value < 10 && unit > 0 ? Number(value.toFixed(1)) : Math.round(value);

  return `${shown} ${units[unit]}`;
}

/**
 * The expected load of an hour, as a multiple of an average hour.
 *
 * @param load - The multiple, or null while Veyra is still learning.
 * @returns For example `1.4×`, or `learning`.
 */
export function formatLoad(load: number | null): string {
  return load === null ? "learning" : `${load.toFixed(1)}×`;
}
