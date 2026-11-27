import type { AlertBlock, AlertDetails } from "../api/types";
import { formatBytes, severityWord } from "../home/homeFormat";
import { formatClock } from "../logs/format";
import { shortDate } from "../time/zone";
import { atMoment, formatDuration } from "./alertFormat";
import {
  containerName,
  containerPath,
  databasePath,
  formatDays,
  formatDiskShare,
  formatMeasure,
  formatThreshold,
  ruleMeasures,
  ruleSentence,
  serverName,
} from "./ruleWords";

/*
 * An alert's figures and state as a strip of figures tells them (ADR 0029): the words of every look, from the alert
 * the server sent. A disk alert tells its disk; any other its measure now, its peak, and its threshold.
 */

/** One figure of an alert. */
export interface AlertFigure {
  /** Its name. */
  label: string;
  /** The figure, worded. */
  value: string;
  /** The line under it. */
  detail: string;
  /** What it shows, for its question mark. */
  help: string;
  /** The frame of the measure while the alert is open: red for a warning, amber for a caution. */
  tone?: "warn" | "bad";
}

/**
 * The figures of an alert, chosen by what it is about: a filling disk its use, growth, and the day it is full; any
 * other alert its measure now or at the end, its peak, its threshold, and how long it has been open.
 *
 * @param alert - The alert.
 * @returns Four figures.
 */
export function alertFigures(alert: AlertDetails): AlertFigure[] {
  const open = alert.resolvedAt === null;
  const tone = open ? (alert.severity === "Warning" ? "bad" : "warn") : undefined;
  const other = alert.severity === "Warning" ? alert.cautionThreshold : alert.warningThreshold;
  const threshold: AlertFigure = {
    label: "Threshold",
    value: formatMeasure(alert.rule, alert.threshold),
    detail: `${severityWord(alert.severity)} · ${alert.severity === "Warning" ? "caution" : "warning"} ${formatThreshold(alert.rule, other)}`,
    help: `${ruleSentence(alert.rule, alert.severity, alert.threshold, alert.windowSeconds)} Admins change the levels on the Settings screen.`,
  };

  if (alert.disk) {
    const { disk } = alert;
    const filling = disk.daysUntilFull !== null && disk.daysUntilFull > 0;
    const growth = disk.ratio !== null && filling ? ((1 - disk.ratio) / (disk.daysUntilFull ?? 1)) * 100 : null;
    const fullOn = filling ? shortDate(Date.parse(alert.updatedAt) + (disk.daysUntilFull ?? 0) * 86_400_000) : null;

    return [
      {
        label: "Used now",
        value: disk.ratio === null ? "—" : formatDiskShare(disk.ratio),
        detail:
          disk.usedBytes !== null && disk.totalBytes !== null
            ? `${formatBytes(disk.usedBytes)} of ${formatBytes(disk.totalBytes)}`
            : "not reported now",
        help: `How much of ${disk.mountpoint} is in use now, of its size.`,
        tone,
      },
      {
        label: "Growing",
        value: growth === null ? "not filling" : `${Number(growth.toFixed(1))} pt a day`,
        detail: "at the pace of the last day",
        help: "By how many percentage points of the disk its use grew in the last day. The forecast assumes it keeps that pace.",
      },
      {
        label: "Full in",
        value: disk.daysUntilFull === null ? "not filling" : formatDays(disk.daysUntilFull),
        detail: fullOn ? `${fullOn}, at this pace` : "at the pace of the last day",
        help: "When the disk is full if it keeps filling at the pace of its last day. The rule judges this against its thresholds.",
        tone,
      },
      threshold,
    ];
  }

  const minutes = Math.round(alert.windowSeconds / 60);
  const end = alert.resolvedAt ?? alert.updatedAt;

  return [
    {
      label: open ? "Now" : "At the end",
      value: formatMeasure(alert.rule, alert.value),
      detail: minutes > 0 ? `over the last ${minutes} min` : `checked ${atMoment(alert.updatedAt, alert.updatedAt)}`,
      help: `${open ? "The value the rule measured at its last check." : "The last value the rule measured before the alert resolved."} It measures: ${ruleMeasures(alert.rule, alert.windowSeconds)}`,
      tone,
    },
    {
      label: "Peak",
      value: formatMeasure(alert.rule, alert.peak),
      detail: alert.peakAt ? atMoment(alert.peakAt, alert.updatedAt) : "time not recorded",
      help: `The worst the measure has been while the alert was open${alert.lowerIsWorse ? ", here its lowest" : ""}, and when it was measured.`,
    },
    threshold,
    {
      label: open ? "Open for" : "Lasted",
      value: formatDuration(Date.parse(end) - Date.parse(alert.openedAt)),
      detail: open ? `since ${formatClock(alert.openedAt)}` : `${formatClock(alert.openedAt)}–${formatClock(end)}`,
      help: open
        ? "How long the alert has been open, to its last check. It resolves by itself once the measure is back under its threshold."
        : "How long the alert was open, from when it opened to when it resolved.",
    },
  ];
}

/**
 * An alert's state in a pill.
 *
 * @param alert - The alert.
 * @returns The words, and the tone: red for a warning, amber for a caution, grey once resolved.
 */
export function alertState(alert: AlertDetails): { words: string; tone: "bad" | "warn" | "off" } {
  if (alert.resolvedAt !== null) {
    return { words: "Resolved", tone: "off" };
  }

  return { words: `${alert.severity} · open`, tone: alert.severity === "Warning" ? "bad" : "warn" };
}

/**
 * Where an alert's block is shown, and the words of a link to it.
 *
 * @param block - The block the alert is about.
 * @returns The path and the link's words.
 */
export function blockLink(block: AlertBlock): { to: string; label: string } {
  switch (block.kind) {
    case "database":
      return { to: databasePath(block.name), label: `Open ${serverName(block.name)}` };
    case "container":
      return { to: containerPath(block), label: `Open ${containerName(block)}` };
    case "host":
      return { to: `/metrics/hosts/${encodeURIComponent(block.name)}`, label: `Open host ${block.name}` };
    default:
      return { to: `/metrics/services/${encodeURIComponent(block.name)}`, label: `Open ${block.name}` };
  }
}
