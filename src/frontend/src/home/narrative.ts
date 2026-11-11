import { alertTitle, subjectLabel } from "../alerts/ruleWords";
import type { HomeSnapshot } from "../api/types";
import { formatClock } from "../logs/format";
import { formatRatio } from "./homeFormat";

/**
 * One line that reads the snapshot like a weather report: the worst alert, the showery ones, and the dry services.
 *
 * @param snapshot - The home snapshot.
 * @returns The sentence or sentences.
 */
export function narrative(snapshot: HomeSnapshot): string {
  const { alerts } = snapshot;
  const services = snapshot.blocks.filter((block) => block.kind === "service");

  if (services.length === 0 && alerts.length === 0) {
    return "No logs arrived in the last hour.";
  }

  const dry = services.filter((block) => block.health === "Ok").length;
  const [only] = services;

  if (alerts.length === 0) {
    return `No alerts are open. ${services.length === 1 && only ? `${only.name} is dry.` : `All ${dry} services are dry.`}`;
  }

  const parts: string[] = [];
  const [worst, ...otherWarnings] = alerts.filter((alert) => alert.severity === "Warning");
  const cautions = alerts.filter((alert) => alert.severity === "Caution");
  const [firstCaution] = cautions;

  if (worst) {
    const more = otherWarnings.length;
    const tail = more === 0 ? "." : `, and ${more} more ${more === 1 ? "alert is" : "alerts are"} stormy.`;
    const what =
      worst.rule === "error-rate"
        ? `${worst.name} fails ${formatRatio(worst.value)} of the time`
        : alertTitle(worst.rule, worst, worst.value, false);

    parts.push(`${what} since ${formatClock(worst.since)}${tail}`);
  }

  if (cautions.length === 1 && firstCaution?.kind === "host") {
    // A disk tells more than "web-01 is showery": say what is coming.
    parts.push(`${alertTitle(firstCaution.rule, firstCaution, firstCaution.value, false)} at today's pace.`);
  } else if (cautions.length > 0) {
    const names = cautions.map((alert) => subjectLabel(alert));

    parts.push(`${joinNames(names)} ${names.length === 1 ? "is" : "are"} showery.`);
  }

  if (services.length > 0) {
    parts.push(`${dry} of ${services.length} services ${dry === 1 ? "is" : "are"} dry.`);
  }

  return parts.join(" ");
}

/** Joins up to three names in English; more become a count. */
function joinNames(names: string[]): string {
  if (names.length > 3) {
    return `${names.length} alerts`;
  }

  const last = names.at(-1) ?? "";

  return names.length <= 1 ? last : `${names.slice(0, -1).join(", ")} and ${last}`;
}
