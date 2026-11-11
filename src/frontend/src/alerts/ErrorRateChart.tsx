import { useId, useRef } from "react";

import type { AlertEvent, AlertPoint } from "../api/types";
import { formatRatio } from "../home/homeFormat";
import { formatClock } from "../logs/format";
import type { ErrorRateAlert } from "./alertFormat";
import { timeTicks, useWidth } from "./chartParts";

/** Props for {@link ErrorRateChart}. */
interface ErrorRateChartProps {
  /** The alert, with its series, thresholds, and events. */
  alert: ErrorRateAlert;
}

/** The chart's height in pixels: the plot, the time axis, and the strip of record counts. */
const height = 268;
const plotTop = 20;
const plotBottom = 210;
const stripTop = 232;
const stripBottom = 262;
const left = 44;
const right = 12;

/** Widths below this drop the labels on the markers; they would collide on a phone. */
const labelWidth = 560;

/** Tops for the scale, each with the number of grid steps that keeps every label round. */
const niceTops: readonly (readonly [number, number])[] = [
  [0.02, 2],
  [0.05, 5],
  [0.1, 2],
  [0.2, 4],
  [0.3, 3],
  [0.5, 5],
  [0.75, 3],
  [1, 4],
];

/**
 * The error rate of the alert's service around the alert, as the rule measured it: the ratio over its window at every
 * point, the caution and warning thresholds, the moments the alert changed, its peak, and the record counts below.
 * It follows the width of its panel, so text stays readable from desktop to phone.
 */
export function ErrorRateChart({ alert }: ErrorRateChartProps) {
  const box = useRef<HTMLDivElement>(null);
  const width = useWidth(box);
  const gradientId = useId();
  const { points } = alert.series;

  const from = new Date(alert.series.from).getTime();
  const to = new Date(alert.series.to).getTime();
  const highest = Math.max(alert.warningThreshold * 1.6, ...points.map((point) => point.ratio ?? 0)) * 1.1;
  const [top, divisions] = niceTops.find(([step]) => step >= highest) ?? [1, 4];
  const shares = Array.from({ length: divisions + 1 }, (_, index) => index / divisions);
  const plotRight = width - right;

  const x = (instant: string | number) =>
    left + ((new Date(instant).getTime() - from) / Math.max(1, to - from)) * (plotRight - left);
  const y = (ratio: number) => plotBottom - (Math.min(ratio, top) / top) * (plotBottom - plotTop);

  const line = linePath(points, x, y);
  const area = areaPath(points, x, y);
  const mostRecords = Math.max(1, ...points.map((point) => point.total));
  const barWidth = Math.max(1, ((plotRight - left) / Math.max(1, points.length)) * 0.7);
  const showLabels = width >= labelWidth;
  const changes = alert.events.filter((event) => event.kind !== "Eased");
  const peakAt = alert.peakAt && new Date(alert.peakAt).getTime() >= from ? alert.peakAt : null;
  const last = points.at(-1);
  const labels = placeLabels(
    changes.map((event) => ({ at: x(event.at), text: changeLabel(event) })),
    plotRight,
  );
  const peakLabel = peakAt ? placePeak(x(peakAt), y(alert.peak), plotRight) : null;

  const summary =
    `Error rate of ${alert.block.name} from ${formatClock(alert.series.from)} to ${formatClock(alert.series.to)} UTC` +
    `, peak ${formatRatio(alert.peak)}${peakAt ? ` at ${formatClock(peakAt)}` : ""}` +
    `, warning from ${formatRatio(alert.warningThreshold)}, caution from ${formatRatio(alert.cautionThreshold)}.`;

  return (
    <div className="rate-chart" ref={box}>
      <svg width={width} height={height} role="img" aria-label={summary}>
        <defs>
          <linearGradient id={gradientId} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" stopColor="var(--fc-red)" stopOpacity="0.5" />
            <stop offset="1" stopColor="var(--fc-red)" stopOpacity="0" />
          </linearGradient>
        </defs>

        <rect
          className="band-warning"
          x={left}
          y={plotTop}
          width={plotRight - left}
          height={y(alert.warningThreshold) - plotTop}
        />
        <rect
          className="band-caution"
          x={left}
          y={y(alert.warningThreshold)}
          width={plotRight - left}
          height={y(alert.cautionThreshold) - y(alert.warningThreshold)}
        />

        {shares.map((share) => (
          <g key={share}>
            <line className="grid" x1={left} x2={plotRight} y1={y(top * share)} y2={y(top * share)} />
            <text className="axis" x={left - 8} y={y(top * share) + 4} textAnchor="end">
              {share === 0 ? "0" : formatRatio(top * share)}
            </text>
          </g>
        ))}

        <rect
          className="episode"
          x={x(alert.openedAt)}
          y={plotTop}
          width={Math.max(0, x(alert.resolvedAt ?? alert.series.to) - x(alert.openedAt))}
          height={plotBottom - plotTop}
        />

        <line
          className="threshold warning"
          x1={left}
          x2={plotRight}
          y1={y(alert.warningThreshold)}
          y2={y(alert.warningThreshold)}
        />
        <line
          className="threshold caution"
          x1={left}
          x2={plotRight}
          y1={y(alert.cautionThreshold)}
          y2={y(alert.cautionThreshold)}
        />
        <text className="threshold-label" x={plotRight - 4} y={y(alert.warningThreshold) - 6} textAnchor="end">
          warning {formatRatio(alert.warningThreshold)}
        </text>

        {area && <path className="rate-area" d={area} fill={`url(#${gradientId})`} />}
        {line && <path className="rate-line" d={line} />}

        {changes.map((event, index) => {
          const at = x(event.at);
          const tone = event.kind === "Resolved" ? "resolved" : event.severity === "Warning" ? "warning" : "caution";
          const label = labels[index];

          return (
            <g key={`${event.kind}-${event.at}`} className={`marker ${tone}`}>
              <line x1={at} x2={at} y1={plotTop} y2={plotBottom} />
              <circle cx={at} cy={y(event.value)} r="5" />
              {showLabels && label && (
                <text x={label.x} y={label.y} textAnchor={label.anchor}>
                  {label.text}
                </text>
              )}
            </g>
          );
        })}

        {peakAt && (
          <g className="peak">
            <circle cx={x(peakAt)} cy={y(alert.peak)} r="6" />
            {showLabels && peakLabel && (
              <text x={peakLabel.x} y={peakLabel.y} textAnchor={peakLabel.anchor}>
                peak {formatRatio(alert.peak)}
              </text>
            )}
          </g>
        )}

        {!alert.resolvedAt && last?.ratio != null && (
          <g className="now">
            <circle cx={x(last.at)} cy={y(last.ratio)} r="5" />
            <circle className="ring" cx={x(last.at)} cy={y(last.ratio)} r="11" />
          </g>
        )}

        {points.map((point) => {
          const barHeight = Math.max(1, (point.total / mostRecords) * (stripBottom - stripTop));

          return (
            <rect
              key={point.at}
              className="records"
              x={x(point.at) - barWidth}
              y={stripBottom - barHeight}
              width={barWidth}
              height={barHeight}
            />
          );
        })}

        {timeTicks(from, to, width - left - right).map((tick) => (
          <text key={tick} className="axis" x={x(tick)} y={plotBottom + 16} textAnchor="middle">
            {formatClock(new Date(tick))}
          </text>
        ))}
      </svg>
    </div>
  );
}

/** A label's place on the chart. */
interface PlacedLabel {
  /** The words. */
  text: string;
  /** Where the text is anchored. */
  x: number;
  /** Its baseline. */
  y: number;
  /** Which end of the text sits at {@link x}. */
  anchor: "start" | "end" | "middle";
}

/** About how wide one character of a marker label is, in pixels. */
const characterWidth = 6.6;

/** The words beside the line of an alert change. */
function changeLabel(event: AlertEvent): string {
  const clock = formatClock(event.at);

  switch (event.kind) {
    case "Resolved":
      return `${clock} resolved`;
    case "Opened":
      return `${clock} opened`;
    default:
      return `${clock} ${event.severity === "Warning" ? "warning" : "caution"}`;
  }
}

/**
 * Places the labels of the alert's changes in two rows at the top of the plot, each beside its line, so labels of
 * changes close together never overlap. A label that fits in neither row is left out; the timeline has it.
 */
function placeLabels(markers: { at: number; text: string }[], plotRight: number): (PlacedLabel | null)[] {
  const rows = [plotTop + 16, plotTop + 32];
  const taken: { from: number; to: number; row: number }[] = [];

  return markers.map((marker) => {
    const width = marker.text.length * characterWidth;
    const sides = marker.at + 6 + width <= plotRight ? (["start", "end"] as const) : (["end", "start"] as const);

    for (const row of rows) {
      for (const anchor of sides) {
        const from = anchor === "start" ? marker.at + 6 : marker.at - 6 - width;
        const to = from + width;
        const fits = from >= left && to <= plotRight;
        const free = taken.every((span) => span.row !== row || to + 8 < span.from || from > span.to + 8);

        if (fits && free) {
          taken.push({ from, to, row });

          return { text: marker.text, x: anchor === "start" ? marker.at + 6 : marker.at - 6, y: row, anchor };
        }
      }
    }

    return null;
  });
}

/** Places the peak label above its point, or beside it when the point is high, inside the plot's edges. */
function placePeak(at: number, height: number, plotRight: number): PlacedLabel {
  const text = "peak";
  const high = height < plotTop + 44;

  if (high) {
    const anchor = at > plotRight - 80 ? "end" : "start";

    return { text, x: anchor === "end" ? at - 12 : at + 12, y: height + 4, anchor };
  }

  const anchor = at > plotRight - 40 ? "end" : at < left + 40 ? "start" : "middle";

  return { text, x: at, y: height - 12, anchor };
}

/** The line through every point with a ratio; a point without records breaks it. */
function linePath(points: AlertPoint[], x: (at: string) => number, y: (ratio: number) => number): string {
  let path = "";
  let drawing = false;

  for (const point of points) {
    if (point.ratio === null) {
      drawing = false;
      continue;
    }

    path += `${drawing ? "L" : "M"}${x(point.at).toFixed(1)} ${y(point.ratio).toFixed(1)} `;
    drawing = true;
  }

  return path.trim();
}

/** The shaded area under the line, one closed shape per unbroken run of points. */
function areaPath(points: AlertPoint[], x: (at: string) => number, y: (ratio: number) => number): string {
  const runs: AlertPoint[][] = [];
  let run: AlertPoint[] = [];

  for (const point of points) {
    if (point.ratio === null) {
      if (run.length > 0) {
        runs.push(run);
      }

      run = [];
    } else {
      run.push(point);
    }
  }

  if (run.length > 0) {
    runs.push(run);
  }

  return runs
    .map((part) => {
      const first = part[0];
      const final = part.at(-1);

      if (!first || !final) {
        return "";
      }

      const top = part.map((point) => `L${x(point.at).toFixed(1)} ${y(point.ratio ?? 0).toFixed(1)}`).join(" ");

      return `M${x(first.at).toFixed(1)} ${plotBottom} ${top} L${x(final.at).toFixed(1)} ${plotBottom} Z`;
    })
    .join(" ");
}
