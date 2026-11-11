import type { AlertDelivery, AlertDetails, AlertEvent, AlertSeverity } from "../api/types";
import type { ShellScene } from "../app/shellContext";
import { severityWord, formatRatio } from "../home/homeFormat";
import { formatClock } from "../logs/format";
import { alertTitle, formatDays, formatDiskShare, formatMeasure, notFillingDays } from "./ruleWords";

/** An error-rate alert, which always has its error-rate series. */
export type ErrorRateAlert = AlertDetails & { series: NonNullable<AlertDetails["series"]> };

/**
 * Whether an alert is an error-rate alert with its series.
 *
 * @param alert - The alert.
 * @returns True for an error-rate alert.
 */
export function isErrorRate(alert: AlertDetails): alert is ErrorRateAlert {
  return alert.rule === "error-rate" && alert.series !== null;
}

/*
 * Words of the alert details screen. Times are UTC, like everywhere in Veyra. The same severity words as the home:
 * warning (red) and caution (amber).
 */

/**
 * A length of time the way people say it.
 *
 * @param milliseconds - The length.
 * @returns For example `36 min`, `1 h 36 min`, or `2 d 3 h`.
 */
export function formatDuration(milliseconds: number): string {
  const minutes = Math.max(1, Math.round(milliseconds / 60_000));
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (days > 0) {
    const rest = hours % 24;

    return rest === 0 ? `${days} d` : `${days} d ${rest} h`;
  }

  if (hours > 0) {
    const rest = minutes % 60;

    return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
  }

  return `${minutes} min`;
}

/**
 * The title of the screen.
 *
 * @param alert - The alert.
 * @returns For example `payments-gateway logs 21.6% errors`, or once resolved `checkout-web is back to p95 420 ms`.
 */
export function headline(alert: AlertDetails): string {
  if (!alert.resolvedAt) {
    return alertTitle(alert.rule, alert.block, alert.value, false);
  }

  const disk = `Disk ${alert.block.part ?? ""} on ${alert.block.name}`;

  switch (alert.rule) {
    case "latency-p95":
      return `${alert.block.name} is back to p95 ${formatMeasure(alert.rule, alert.value)}`;
    case "disk-space":
      return `${disk} is back to ${formatDiskShare(alert.value)} full`;
    case "disk-filling":
      return alert.value >= notFillingDays
        ? `${disk} is no longer filling`
        : `${disk} is full in ${formatDays(alert.value)} now`;
    case "error-rate":
      return `${alert.block.name} is back to ${formatRatio(alert.value)} errors`;
  }
}

/**
 * The state chip above the title.
 *
 * @param alert - The alert.
 * @param now - The current time, for the age of an open alert.
 * @returns For example `Open · warning · 36 min`.
 */
export function stateLine(alert: AlertDetails, now: Date): string {
  const opened = new Date(alert.openedAt).getTime();

  if (alert.resolvedAt) {
    const lasted = formatDuration(new Date(alert.resolvedAt).getTime() - opened);

    return `Resolved · was ${severityWord(alert.worst)} · lasted ${lasted}`;
  }

  return `Open · ${severityWord(alert.severity)} · ${formatDuration(now.getTime() - opened)}`;
}

/**
 * The sentence under the title: when each threshold was crossed, and when the alert clears or cleared.
 *
 * @param alert - The alert.
 * @returns One or two sentences.
 */
export function alertStory(alert: AlertDetails): string {
  const where = alert.environment ? ` in ${alert.environment}` : "";
  const crossings = alert.events
    .filter((event) => event.kind === "Opened" || event.kind === "Escalated")
    .map((event) => `${formatMeasure(alert.rule, thresholdOf(alert, event.severity))} at ${formatClock(event.at)}`);
  const minutes = Math.round(alert.windowSeconds / 60);
  const cleared = alert.resolvedAt ? formatClock(alert.resolvedAt) : null;

  switch (alert.rule) {
    case "disk-filling": {
      if (cleared) {
        const how =
          alert.value >= notFillingDays
            ? "when the disk stopped filling"
            : "when the forecast moved past " + formatDays(alert.clearThreshold);

        return `The disk came within ${crossings.join(" and within ")} of full at the pace of its last day. It cleared at ${cleared}, ${how}.`;
      }

      const ratio = alert.disk?.ratio ?? null;
      const growth = ratio !== null && alert.value > 0 ? ((1 - ratio) / alert.value) * 100 : null;
      const pace =
        ratio !== null && growth !== null
          ? `At the pace of the last day, ${alert.block.part ?? "the disk"} grows ${Number(growth.toFixed(1))} points a day and is ${formatDiskShare(ratio)} used now. `
          : "";

      return (
        `${pace}Caution within ${formatDays(alert.cautionThreshold)}, warning within ${formatDays(alert.warningThreshold)}. ` +
        `It clears once the forecast is beyond ${formatDays(alert.clearThreshold)} or the disk stops growing.`
      );
    }
    case "disk-space": {
      const rose = `The disk${where} went above ${crossings.join(" and above ")}.`;
      const below = formatDiskShare(clearsBelow(alert));

      if (cleared) {
        return `${rose} It cleared at ${cleared}, below ${below}.`;
      }

      return alert.severity === "Warning"
        ? `${rose} It eases to caution below ${below} and clears below ${formatDiskShare(alert.clearThreshold)} in use.`
        : `${rose} It clears below ${below} in use.`;
    }
    case "latency-p95": {
      const rose = `The p95 of its requests${where} went above ${crossings.join(" and above ")}.`;
      const below = formatMeasure(alert.rule, clearsBelow(alert));

      return cleared
        ? `${rose} It cleared at ${cleared}, below ${below}.`
        : `${rose} ${staysBelow(alert, (value) => formatMeasure(alert.rule, value), minutes)}`;
    }
    case "error-rate": {
      const rose = `The error rate${where} went above ${crossings.join(" and above ")}.`;
      const below = formatRatio(clearsBelow(alert));

      return cleared
        ? `${rose} It cleared at ${cleared}, below ${below}.`
        : `${rose} ${staysBelow(alert, formatRatio, minutes)}`;
    }
  }
}

/**
 * How an open alert ends: a caution clears below its clear threshold; a warning first eases to caution.
 *
 * @param alert - The open alert.
 * @param format - Writes a value in the rule's unit.
 * @param minutes - The rule's window.
 * @returns The sentence.
 */
function staysBelow(alert: AlertDetails, format: (value: number) => string, minutes: number): string {
  const span = `for ${minutes} ${minutes === 1 ? "minute" : "minutes"}`;

  return alert.severity === "Warning"
    ? `It eases to caution below ${format(clearsBelow(alert))} and clears when it stays below ${format(alert.clearThreshold)} ${span}.`
    : `It clears when it stays below ${format(clearsBelow(alert))} ${span}.`;
}

/**
 * Where the alert's current severity ends: the rule's caution clears at its clear threshold, and a warning at the
 * same distance from its own threshold.
 *
 * @param alert - The alert.
 * @returns The value, in the rule's unit.
 */
export function clearsBelow(alert: AlertDetails): number {
  if (alert.rule === "disk-space") {
    return alert.threshold - (alert.cautionThreshold - alert.clearThreshold);
  }

  return alert.threshold * (alert.clearThreshold / alert.cautionThreshold);
}

/**
 * The threshold of a severity, as the rule is set now.
 *
 * @param alert - The alert, with the rule's thresholds.
 * @param severity - Warning or caution.
 * @returns The ratio.
 */
export function thresholdOf(alert: AlertDetails, severity: AlertSeverity): number {
  return severity === "Warning" ? alert.warningThreshold : alert.cautionThreshold;
}

/**
 * The title of a timeline entry.
 *
 * @param event - The change.
 * @returns For example `Opened as caution` or `Rose to warning`.
 */
export function eventTitle(event: AlertEvent): string {
  switch (event.kind) {
    case "Opened":
      return `Opened as ${severityWord(event.severity)}`;
    case "Escalated":
      return `Rose to ${severityWord(event.severity)}`;
    case "Eased":
      return `Eased to ${severityWord(event.severity)}`;
    case "Resolved":
      return "Resolved";
  }
}

/**
 * The short state of a message, for the tag beside it.
 *
 * @param delivery - The message.
 * @returns The words and their tone: ok (green), wait (neutral), or bad (red).
 */
export function deliveryTag(delivery: AlertDelivery): { text: string; tone: "ok" | "wait" | "bad" } {
  switch (delivery.state) {
    case "Sent":
      return {
        text: delivery.lastStatus === null ? "sent" : `delivered · ${delivery.lastStatus}`,
        tone: "ok",
      };
    case "Failed":
      return { text: "failed", tone: "bad" };
    case "Pending":
      return delivery.attempts === 0 ? { text: "queued", tone: "wait" } : { text: "retrying", tone: "bad" };
  }
}

/**
 * The line under a message that did not go through: what went wrong and what happens next.
 *
 * @param delivery - The message.
 * @returns The line, or null when there is nothing wrong to tell.
 */
export function deliveryNote(delivery: AlertDelivery): string | null {
  if (delivery.state === "Failed") {
    return `Gave up after ${delivery.attempts} tries: ${delivery.lastError ?? "unknown error"}.`;
  }

  if (delivery.state === "Pending" && delivery.attempts > 0) {
    const next = delivery.nextAttemptAt ? ` Next try ${formatClock(delivery.nextAttemptAt)}.` : "";

    return `Try ${delivery.attempts} failed: ${delivery.lastError ?? "unknown error"}.${next}`;
  }

  return null;
}

/**
 * The weather behind the screen: the alert's own, or clear skies once it resolved.
 *
 * @param alert - The alert.
 * @returns The scene.
 */
export function alertScene(alert: AlertDetails): ShellScene {
  if (alert.resolvedAt) {
    return "clear";
  }

  return alert.severity === "Warning" ? "storm" : "showers";
}
