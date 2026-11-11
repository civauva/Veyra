import { useRef } from "react";

import type { AlertDetails, AlertLatency } from "../api/types";
import { formatClock } from "../logs/format";
import { formatSeconds } from "../metrics/metricFormat";
import { besideLine, gappedPath, niceScale, timeTicks, useWidth } from "./chartParts";

/** Props for {@link LatencyChart}. */
interface LatencyChartProps {
  /** The alert, a latency one. */
  alert: AlertDetails;
  /** Its service's request durations around it. */
  latency: AlertLatency;
}

const height = 268;
const plotTop = 20;
const plotBottom = 210;
const stripTop = 232;
const stripBottom = 262;
const left = 50;
const right = 12;

/** Widths below this drop the labels on the markers; they would collide on a phone. */
const labelWidth = 560;

/**
 * The 95th percentile and the median of the service's request durations around the alert, per minute, with the caution
 * and warning thresholds, the moments it opened and resolved, its peak, and the requests per minute below.
 */
export function LatencyChart({ alert, latency }: LatencyChartProps) {
  const box = useRef<HTMLDivElement>(null);
  const width = useWidth(box);

  const { points } = latency;
  const from = new Date(latency.from).getTime();
  const to = new Date(latency.to).getTime();
  const highest = Math.max(alert.warningThreshold * 1.25, ...points.map((point) => point.p95Seconds ?? 0)) * 1.05;
  const { top, step } = niceScale(highest);
  const plotRight = width - right;

  const x = (instant: string | number) =>
    left + ((new Date(instant).getTime() - from) / Math.max(1, to - from)) * (plotRight - left);
  const y = (seconds: number) => plotBottom - (Math.min(seconds, top) / top) * (plotBottom - plotTop);

  const p95 = gappedPath(
    points.map((point) => ({ at: point.at, value: point.p95Seconds })),
    x,
    y,
  );
  const p50 = gappedPath(
    points.map((point) => ({ at: point.at, value: point.p50Seconds })),
    x,
    y,
  );
  const mostRequests = Math.max(1, ...points.map((point) => point.requests));
  const barWidth = Math.max(1, ((plotRight - left) / Math.max(1, points.length)) * 0.7);
  const showLabels = width >= labelWidth;
  const peakAt = alert.peakAt && new Date(alert.peakAt).getTime() >= from ? alert.peakAt : null;
  const grid = Array.from({ length: Math.round(top / step) + 1 }, (_, index) => index * step);
  const last = points.findLast((point) => point.p95Seconds !== null);

  const summary =
    `Request durations of ${alert.block.name} from ${formatClock(latency.from)} to ${formatClock(latency.to)} UTC` +
    `, peak p95 ${formatSeconds(alert.peak)}${peakAt ? ` at ${formatClock(peakAt)}` : ""}` +
    `, warning from ${formatSeconds(alert.warningThreshold)}, caution from ${formatSeconds(alert.cautionThreshold)}.`;

  return (
    <div className="rate-chart" ref={box}>
      <svg width={width} height={height} role="img" aria-label={summary}>
        {grid.map((value) => (
          <g key={value}>
            <line className="grid" x1={left} x2={plotRight} y1={y(value)} y2={y(value)} />
            <text className="axis" x={left - 8} y={y(value) + 4} textAnchor="end">
              {value === 0 ? "0" : formatSeconds(value)}
            </text>
          </g>
        ))}

        <rect
          className="episode"
          x={x(alert.openedAt)}
          y={plotTop}
          width={Math.max(0, x(alert.resolvedAt ?? latency.to) - x(alert.openedAt))}
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

        {p50 && <path className="p50-line" d={p50} />}
        {p95 && <path className="p95-line" d={p95} />}

        <g className="marker caution">
          <line x1={x(alert.openedAt)} x2={x(alert.openedAt)} y1={plotTop} y2={plotBottom} />
          {showLabels && (
            <text {...besideLine(x(alert.openedAt), plotRight)} y={plotBottom - 10}>
              {formatClock(alert.openedAt)} opened
            </text>
          )}
        </g>
        {alert.resolvedAt && (
          <g className="marker resolved">
            <line x1={x(alert.resolvedAt)} x2={x(alert.resolvedAt)} y1={plotTop} y2={plotBottom} />
            {showLabels && (
              <text {...besideLine(x(alert.resolvedAt), plotRight)} y={plotTop + 16}>
                {formatClock(alert.resolvedAt)} resolved
              </text>
            )}
          </g>
        )}

        {peakAt && (
          <g className="peak">
            <circle cx={x(peakAt)} cy={y(alert.peak)} r="6" />
            {showLabels && (
              <text x={x(peakAt)} y={y(alert.peak) - 12} textAnchor="middle">
                peak {formatSeconds(alert.peak)}
              </text>
            )}
          </g>
        )}

        {!alert.resolvedAt && last?.p95Seconds != null && (
          <g className="now">
            <circle cx={x(last.at)} cy={y(last.p95Seconds)} r="5" />
            <circle className="ring" cx={x(last.at)} cy={y(last.p95Seconds)} r="11" />
          </g>
        )}

        {points.map((point) => {
          const barHeight = Math.max(
            point.requests > 0 ? 1 : 0,
            (point.requests / mostRequests) * (stripBottom - stripTop),
          );

          return (
            <rect
              key={point.at}
              className="records"
              x={x(point.at)}
              y={stripBottom - barHeight}
              width={barWidth}
              height={barHeight}
            />
          );
        })}

        {timeTicks(from, to, plotRight - left).map((tick) => (
          <text key={tick} className="axis" x={x(tick)} y={plotBottom + 16} textAnchor="middle">
            {formatClock(new Date(tick))}
          </text>
        ))}
      </svg>
    </div>
  );
}
