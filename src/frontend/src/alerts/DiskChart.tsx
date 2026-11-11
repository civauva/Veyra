import { useId, useRef } from "react";

import type { AlertDetails, AlertDisk } from "../api/types";
import { formatClock } from "../logs/format";
import { besideLine, timeTicks, useWidth } from "./chartParts";
import { formatDiskShare } from "./ruleWords";

/** Props for {@link DiskChart}. */
interface DiskChartProps {
  /** The alert, a disk-space or disk-filling one. */
  alert: AlertDetails;
  /** Its disk, with the week of use. */
  disk: AlertDisk;
}

const height = 260;
const plotTop = 18;
const plotBottom = 222;
const left = 44;
const right = 14;

/** Widths below this drop the labels on the markers; they would collide on a phone. */
const labelWidth = 560;

/** The forecast is drawn at most this far ahead; a disk full later is not soon. */
const forecastLimitDays = 30;

const dayMilliseconds = 86_400_000;

/**
 * The share of a disk in use over the week before the alert and since, with, while it fills, the forecast at the pace
 * of the last day until it is full. Disk-space alerts show their thresholds; both show the moment the alert opened.
 */
export function DiskChart({ alert, disk }: DiskChartProps) {
  const box = useRef<HTMLDivElement>(null);
  const width = useWidth(box);
  const gradientId = useId();

  const from = new Date(disk.from).getTime();
  const to = new Date(disk.to).getTime();
  const days = disk.daysUntilFull !== null && disk.daysUntilFull <= forecastLimitDays ? disk.daysUntilFull : null;
  const filling = alert.resolvedAt === null && disk.ratio !== null && days !== null;
  const fullAt = filling && days !== null ? to + days * dayMilliseconds : null;
  const end = fullAt === null ? to : Math.max(to, fullAt + dayMilliseconds / 2);
  const plotRight = width - right;

  const x = (instant: string | number) =>
    left + ((new Date(instant).getTime() - from) / Math.max(1, end - from)) * (plotRight - left);
  const y = (ratio: number) => plotBottom - Math.min(Math.max(ratio, 0), 1) * (plotBottom - plotTop);

  const measured = disk.points.filter((point): point is { at: string; ratio: number } => point.ratio !== null);
  const line = runs(disk.points)
    .map((run) =>
      run.map((point, index) => `${index ? "L" : "M"}${x(point.at).toFixed(1)} ${y(point.ratio).toFixed(1)}`).join(" "),
    )
    .join(" ");
  const area = runs(disk.points)
    .map((run) => {
      const first = run[0];
      const final = run.at(-1);

      return first && final
        ? `M${x(first.at).toFixed(1)} ${plotBottom} ${run.map((point) => `L${x(point.at).toFixed(1)} ${y(point.ratio).toFixed(1)}`).join(" ")} L${x(final.at).toFixed(1)} ${plotBottom} Z`
        : "";
    })
    .join(" ");
  const showLabels = width >= labelWidth;
  const space = alert.rule === "disk-space";
  const last = measured.at(-1);

  const summary =
    `Disk ${disk.mountpoint} on ${alert.block.name} in use from ${formatDay(from)} to ${formatDay(to)}` +
    (disk.ratio === null ? "" : `, ${formatDiskShare(disk.ratio)} now`) +
    (fullAt === null ? "." : `, full on ${formatDay(fullAt)} at the pace of the last day.`);

  return (
    <div className="rate-chart" ref={box}>
      <svg width={width} height={height} role="img" aria-label={summary}>
        <defs>
          <linearGradient id={gradientId} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" stopColor="var(--fc-amber)" stopOpacity="0.4" />
            <stop offset="1" stopColor="var(--fc-amber)" stopOpacity="0" />
          </linearGradient>
        </defs>

        {space && (
          <>
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
          </>
        )}

        {[0, 0.25, 0.5, 0.75, 1].map((share) => (
          <g key={share}>
            <line className="grid" x1={left} x2={plotRight} y1={y(share)} y2={y(share)} />
            <text className="axis" x={left - 8} y={y(share) + 4} textAnchor="end">
              {share * 100}%
            </text>
          </g>
        ))}

        {fullAt !== null && (
          <>
            <rect
              className="forecast-band"
              x={x(to)}
              y={plotTop}
              width={plotRight - x(to)}
              height={plotBottom - plotTop}
            />
            {showLabels && (
              <text className="forecast-label" x={x(to) + 8} y={plotTop + 14}>
                forecast at the pace of the last day
              </text>
            )}
          </>
        )}

        <line className="threshold full" x1={left} x2={plotRight} y1={y(1)} y2={y(1)} />
        {space && (
          <>
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
          </>
        )}

        {area && <path className="disk-area" d={area} fill={`url(#${gradientId})`} />}
        {line && <path className="disk-line" d={line} />}

        <g className={`marker ${alert.severity === "Warning" ? "warning" : "caution"}`}>
          <line x1={x(alert.openedAt)} x2={x(alert.openedAt)} y1={plotTop} y2={plotBottom} />
          {showLabels && (
            <text x={x(alert.openedAt) - 6} y={plotBottom - 10} textAnchor="end">
              {formatClock(alert.openedAt)} opened
            </text>
          )}
        </g>
        {alert.resolvedAt && (
          <g className="marker resolved">
            <line x1={x(alert.resolvedAt)} x2={x(alert.resolvedAt)} y1={plotTop} y2={plotBottom} />
            {showLabels && (
              <text {...besideLine(x(alert.resolvedAt), plotRight)} y={plotTop + 14}>
                {formatClock(alert.resolvedAt)} resolved
              </text>
            )}
          </g>
        )}

        {fullAt !== null && disk.ratio !== null && (
          <g className="forecast">
            <path
              className="forecast-line"
              d={`M${x(to).toFixed(1)} ${y(disk.ratio).toFixed(1)} L${x(fullAt).toFixed(1)} ${y(1).toFixed(1)}`}
            />
            <circle className="full-point" cx={x(fullAt)} cy={y(1)} r="6" />
            <text
              className="full-label"
              x={x(fullAt)}
              y={y(1) + 22}
              textAnchor={x(fullAt) > plotRight - 50 ? "end" : "middle"}
            >
              full {formatDay(fullAt)}
            </text>
          </g>
        )}

        {alert.resolvedAt === null && last && (
          <g className="now">
            <circle cx={x(last.at)} cy={y(last.ratio)} r="5" />
            <circle className="ring" cx={x(last.at)} cy={y(last.ratio)} r="11" />
            {showLabels && (
              <text className="now-label" x={x(last.at) - 12} y={y(last.ratio) + 24} textAnchor="end">
                now {formatDiskShare(last.ratio)}
              </text>
            )}
          </g>
        )}

        {timeTicks(from, end, plotRight - left).map((tick) => (
          <text key={tick} className="axis" x={x(tick)} y={plotBottom + 16} textAnchor="middle">
            {end - from > 2 * dayMilliseconds ? formatDay(tick) : formatClock(new Date(tick))}
          </text>
        ))}
      </svg>
    </div>
  );
}

/** A day as the chart's axis writes it, in UTC: `Nov 19`. */
function formatDay(at: number): string {
  return new Date(at).toLocaleDateString("en-US", { month: "short", day: "numeric", timeZone: "UTC" });
}

/** The unbroken runs of measured points; a point without a value breaks the line. */
function runs(points: AlertDisk["points"]): { at: string; ratio: number }[][] {
  const all: { at: string; ratio: number }[][] = [];
  let run: { at: string; ratio: number }[] = [];

  for (const point of points) {
    if (point.ratio === null) {
      if (run.length > 0) {
        all.push(run);
      }

      run = [];
    } else {
      run.push({ at: point.at, ratio: point.ratio });
    }
  }

  if (run.length > 0) {
    all.push(run);
  }

  return all;
}
