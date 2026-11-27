import type { ReactNode } from "react";

import type { ChartLine, ChartPoint } from "../../../api/types";
import { MiniLine } from "../../../resources/MiniLine";
import { changeTone, changeWords } from "../../../resources/resourceFormat";
import { HelpTip } from "../../../ui/HelpTip";

/*
 * The small parts the Console SQL Server tabs share (VEY-75): a figure with its line and change, a donut of shares,
 * and lines for the line chart. They only draw what the server worked out (ADR 0025).
 */

/** Props for {@link SqlFigure}. */
export interface SqlFigureProps {
  /** Its name. */
  label: string;
  /** The value, formatted. */
  value: ReactNode;
  /** The line under it: the change, or a fact. */
  detail?: ReactNode;
  /** The value over the range. */
  line?: (number | null)[];
  /** A warning or caution frame. */
  tone?: "warn" | "bad";
  /** What it shows, for its question mark. */
  help: string;
  /** Opens the tab with its details, when it has one; the whole tile is then a button (VEY-82). */
  onOpen?: () => void;
}

/**
 * One figure of a tab's strip: its name and question mark, the value, a line of detail, and a small line. A figure
 * with a tab behind it opens that tab from anywhere on the tile.
 */
export function SqlFigure({ label, value, detail, line, tone, help, onOpen }: SqlFigureProps) {
  return (
    <div className={["cn-figure", "sq-figure", tone].filter(Boolean).join(" ")}>
      <div className="cn-figure-name">
        {onOpen ? (
          <button type="button" className="cn-figure-link" onClick={onOpen}>
            {label}
          </button>
        ) : (
          label
        )}
        <HelpTip label={label}>{help}</HelpTip>
      </div>
      <div className="cn-figure-value">{value}</div>
      {detail && <div className="cn-figure-detail">{detail}</div>}
      {line && line.length > 1 && (
        <MiniLine values={line} tone={tone === "bad" || tone === "warn" ? "bad" : "ok"} className="sq-figure-line" />
      )}
    </div>
  );
}

/**
 * The change against the period before, in words and in its tone.
 *
 * @param change - The change, or null.
 * @param upIsBad - Whether a rise is bad.
 * @param points - Whether it is a ratio, worded in percentage points.
 * @returns The words in a span with the tone's class.
 */
export function Change({
  change,
  upIsBad,
  points = false,
  short = false,
}: {
  change: number | null;
  upIsBad: boolean;
  points?: boolean;
  short?: boolean;
}) {
  const tone = changeTone(change, upIsBad);
  const words = changeWords(change, points);

  // A figure has room for the change alone; its strip says what it is compared with.
  return <span className={`sq-change ${tone}`}>{short ? words.replace(/ vs the period before$/, "") : words}</span>;
}

// The product as people name it lives with the integrations, whose probes name it too (ADR 0028).
export { productName } from "../../../integrations/integrationWords";

/** One share of a donut. */
export interface DonutPart {
  /** Its name. */
  name: string;
  /** Its share, from 0 to 1. */
  share: number;
  /** Its colour, a CSS value. */
  color: string;
}

/**
 * A donut of shares with a figure in its middle, as the waits of reference screenshots 1 and 3.
 *
 * @param props - The parts, the figure in the middle, and a word under it.
 * @returns The donut.
 */
export function Donut({ parts, center, under }: { parts: DonutPart[]; center: string; under: string }) {
  const radius = 46;
  const circumference = 2 * Math.PI * radius;
  // Each arc starts where the parts before it end.
  const starts = parts.map(
    (_, index) => parts.slice(0, index).reduce((sum, part) => sum + part.share, 0) * circumference,
  );

  return (
    <svg className="sq-donut" viewBox="0 0 120 120" role="img" aria-label={`${center} ${under}`}>
      <circle r={radius} cx="60" cy="60" fill="none" stroke="var(--chip)" strokeWidth="14" />
      {parts.map((part, index) => {
        const length = circumference * part.share;

        return (
          <circle
            key={part.name}
            r={radius}
            cx="60"
            cy="60"
            fill="none"
            stroke={part.color}
            strokeWidth="14"
            strokeDasharray={`${Math.max(0, length - 1.5)} ${circumference - Math.max(0, length - 1.5)}`}
            strokeDashoffset={-(starts[index] ?? 0)}
            transform="rotate(-90 60 60)"
          />
        );
      })}
      <text x="60" y="59" textAnchor="middle" className="sq-donut-center">
        {center}
      </text>
      <text x="60" y="75" textAnchor="middle" className="sq-donut-under">
        {under}
      </text>
    </svg>
  );
}

/** The colours of a donut's parts, in order, the last one for "Other". */
export const partColors = [
  "var(--accent)",
  "var(--cn-violet, #a78bfa)",
  "var(--info-fg)",
  "var(--warning-fg)",
  "var(--faint)",
];

/**
 * A line for {@link LineChart} from values spread evenly over a window.
 *
 * @param label - Its name in the legend.
 * @param values - The values, oldest first.
 * @param from - The start of the window, as an ISO instant.
 * @param to - Its end.
 * @param starts - The start of each value's step, when the server gave them.
 * @returns The line.
 */
export function lineOver(
  label: string,
  values: (number | null)[],
  from: string,
  to: string,
  starts?: string[],
): ChartLine {
  const begin = Date.parse(from);
  const step = values.length > 1 ? (Date.parse(to) - begin) / values.length : 0;
  const points: ChartPoint[] = values.map((value, index) => ({
    at: starts?.[index] ?? new Date(begin + step * index).toISOString(),
    value,
  }));

  return { label, latest: values.filter((value) => value !== null).at(-1) ?? null, points };
}

/**
 * A length of time in the largest units that matter: `6 h 12 min`, `42 min`, `56 s`.
 *
 * @param seconds - The seconds, or null.
 * @returns The words, or a dash.
 */
export function timeWords(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }

  if (seconds < 60) {
    return `${Math.round(seconds)} s`;
  }

  if (seconds < 3600) {
    return `${Math.round(seconds / 60)} min`;
  }

  if (seconds < 86_400) {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.round((seconds % 3600) / 60);

    return minutes > 0 ? `${hours} h ${minutes} min` : `${hours} h`;
  }

  const days = Math.floor(seconds / 86_400);
  const hours = Math.round((seconds % 86_400) / 3600);

  return hours > 0 ? `${days} d ${hours} h` : `${days} d`;
}

/**
 * How long a step took, as precise as it matters: `240 ms`, `1.8 s`, `74.2 s`, `3 min`.
 *
 * @param milliseconds - The time, or null.
 * @returns The words, or a dash.
 */
export function stepWords(milliseconds: number | null): string {
  if (milliseconds === null) {
    return "—";
  }

  if (milliseconds < 1000) {
    return `${Math.round(milliseconds)} ms`;
  }

  if (milliseconds < 100_000) {
    return `${(milliseconds / 1000).toFixed(1)} s`;
  }

  return timeWords(milliseconds / 1000);
}

/**
 * A wait as SQL Server shows it, `00:42:19`.
 *
 * @param seconds - The seconds, or null.
 * @returns The clock, or a dash.
 */
export function waitClock(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }

  const whole = Math.round(seconds);
  const pad = (value: number) => String(value).padStart(2, "0");

  return `${pad(Math.floor(whole / 3600))}:${pad(Math.floor((whole % 3600) / 60))}:${pad(whole % 60)}`;
}

/**
 * Growth of an amount, with its sign: `+12.4 GB`, `−1.1 GB`, `0 B`.
 *
 * @param bytes - The growth, or null.
 * @param format - Formats an amount.
 * @returns The words, or a dash.
 */
export function growthWords(bytes: number | null, format: (bytes: number) => string): string {
  if (bytes === null) {
    return "—";
  }

  return bytes > 0 ? `+${format(bytes)}` : bytes < 0 ? `−${format(-bytes)}` : format(0);
}
