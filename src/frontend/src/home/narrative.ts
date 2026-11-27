import type { HomeSnapshot } from "../api/types";

/**
 * One line under the hero that answers "is it all right?" (VEY-26): how many alerts need you, and how many services
 * are dry. The alerts themselves are told once, in Needs you, so the line points there instead of repeating them.
 *
 * @param snapshot - The home snapshot.
 * @returns The sentence or sentences.
 */
export function narrative(snapshot: HomeSnapshot): string {
  // The server groups and counts (VEY-68); the line only words its figures.
  const { alerts } = snapshot;
  const { services, serviceCounts, alertCounts } = snapshot.estate;

  if (services.length === 0 && alerts.length === 0) {
    return "No logs arrived in the last hour.";
  }

  const dry = serviceCounts.ok;
  const [only] = services;

  if (alerts.length === 0) {
    return `No alerts are open. ${services.length === 1 && only ? `${only.name} is dry.` : `All ${dry} services are dry.`}`;
  }

  const { warnings, cautions } = alertCounts;
  const parts = [
    warnings > 0 ? `${warnings} ${warnings === 1 ? "warning" : "warnings"}` : "",
    cautions > 0 ? `${cautions} ${cautions === 1 ? "caution" : "cautions"}` : "",
  ].filter(Boolean);
  const needs = `${parts.join(" and ")} ${alerts.length === 1 ? "needs" : "need"} you.`;

  if (services.length === 0) {
    return needs;
  }

  return `${needs} ${dry} of ${services.length} services ${dry === 1 ? "is" : "are"} dry.`;
}
