import { useLayoutEffect, useMemo, useRef, useState, type PointerEvent } from "react";

import type { TraceDurationPoint } from "../api/types";
import { formatClock } from "../logs/format";
import { formatDuration } from "./traceMath";

/** A window picked by dragging across the chart: a time span and a duration band. */
export interface PickedWindow {
  /** The start of the time span. */
  from: Date;
  /** The end of the time span. */
  to: Date;
  /** The shortest duration in the band, in milliseconds. */
  minMs: number;
  /** The longest duration in the band, in milliseconds. */
  maxMs: number;
}

/** Props for {@link DurationChart}. */
interface DurationChartProps {
  /** The traces to draw: every failed and slow one, and a sample of the rest. */
  points: TraceDurationPoint[];
  /** The 95th percentile, drawn as a line; traces above it are slow. Null when there are no traces. */
  p95Ms: number | null;
  /** How many traces were read, which the points sample. */
  read: number;
  /** How many traces read failed, counted on the server (VEY-68). */
  failed: number;
  /** How many traces read were slow and did not fail, counted on the server (VEY-68). */
  slow: number;
  /** The start of the window on the time axis. */
  from: Date;
  /** The end of the window. */
  to: Date;
  /** The window picked earlier, drawn as a box; null when none is. */
  picked: PickedWindow | null;
  /** Called with the window the person dragged across. */
  onPick: (window: PickedWindow) => void;
  /** Called with the trace the person clicked. */
  onOpen: (point: TraceDurationPoint) => void;
}

const height = 220;
const margin = { left: 52, right: 12, top: 10, bottom: 24 };

/** Durations from 0.1 ms to 10 minutes, at 1-3-10 steps, as the y axis labels. */
const durationTicks = [0.1, 0.3, 1, 3, 10, 30, 100, 300, 1000, 3000, 10_000, 30_000, 100_000, 300_000];

/**
 * Every trace of the window as a dot: when it started and, on a logarithmic scale, how long it took. Failed traces
 * are red and slow ones amber, so an incident shows as a band of colour. Clicking a dot opens its trace; dragging a
 * box picks traces by time and duration. The filter panel offers the same choices by keyboard.
 */
export function DurationChart({
  points,
  p95Ms,
  read,
  failed,
  slow,
  from,
  to,
  picked,
  onPick,
  onOpen,
}: DurationChartProps) {
  const frame = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(800);
  const [drag, setDrag] = useState<{ x0: number; y0: number; x1: number; y1: number } | null>(null);

  useLayoutEffect(() => {
    const element = frame.current;

    if (!element || typeof ResizeObserver === "undefined") {
      return undefined;
    }

    const observer = new ResizeObserver(([entry]) => {
      if (entry) {
        setWidth(Math.max(240, entry.contentRect.width));
      }
    });
    observer.observe(element);

    return () => observer.disconnect();
  }, []);

  const scale = useMemo(() => {
    const durations = points.map((point) => point.durationMs).filter((value) => value > 0);
    const low = Math.max(0.1, Math.min(1, ...durations) / 1.5);
    const high = Math.max(10, ...durations, p95Ms ?? 0) * 1.5;
    const plotWidth = width - margin.left - margin.right;
    const plotHeight = height - margin.top - margin.bottom;
    const start = from.getTime();
    const span = Math.max(1, to.getTime() - start);
    const logLow = Math.log10(low);
    const logSpan = Math.log10(high) - logLow;

    return {
      x: (time: number) => margin.left + ((time - start) / span) * plotWidth,
      y: (ms: number) => margin.top + plotHeight * (1 - (Math.log10(Math.max(ms, low)) - logLow) / logSpan),
      time: (x: number) => start + ((x - margin.left) / plotWidth) * span,
      ms: (y: number) => 10 ** (logLow + (1 - (y - margin.top) / plotHeight) * logSpan),
      ticks: durationTicks.filter((tick) => tick >= low && tick <= high),
    };
  }, [points, p95Ms, from, to, width]);

  const timeTicks = Array.from({ length: 6 }, (_, i) => from.getTime() + ((to.getTime() - from.getTime()) * i) / 5);

  function position(event: PointerEvent<SVGSVGElement>) {
    const box = event.currentTarget.getBoundingClientRect();

    return { x: event.clientX - box.left, y: event.clientY - box.top };
  }

  function finish(event: PointerEvent<SVGSVGElement>) {
    if (!drag) {
      return;
    }

    const end = position(event);
    setDrag(null);

    if (Math.abs(end.x - drag.x0) < 6 && Math.abs(end.y - drag.y0) < 6) {
      // A click: open the nearest trace within reach.
      const nearest = points
        .map((point) => ({
          point,
          distance: Math.hypot(scale.x(Date.parse(point.start)) - end.x, scale.y(point.durationMs) - end.y),
        }))
        .sort((a, b) => a.distance - b.distance)[0];

      if (nearest && nearest.distance < 10) {
        onOpen(nearest.point);
      }

      return;
    }

    onPick({
      from: new Date(scale.time(Math.min(drag.x0, end.x))),
      to: new Date(scale.time(Math.max(drag.x0, end.x))),
      minMs: Math.round(scale.ms(Math.max(drag.y0, end.y))),
      maxMs: Math.round(scale.ms(Math.min(drag.y0, end.y))),
    });
  }

  return (
    <div ref={frame} className="duration-chart">
      <svg
        width={width}
        height={height}
        role="img"
        aria-label={`Duration of ${read} traces: ${failed} failed, ${slow} slower than the 95th percentile${
          p95Ms === null ? "" : ` of ${formatDuration(p95Ms)}`
        }.`}
        onPointerDown={(event) => {
          const start = position(event);
          event.currentTarget.setPointerCapture(event.pointerId);
          setDrag({ x0: start.x, y0: start.y, x1: start.x, y1: start.y });
        }}
        onPointerMove={(event) => {
          if (drag) {
            const at = position(event);
            setDrag({ ...drag, x1: at.x, y1: at.y });
          }
        }}
        onPointerUp={finish}
        onPointerCancel={() => setDrag(null)}
      >
        {scale.ticks.map((tick) => (
          <g key={tick}>
            <line
              className="dc-grid"
              x1={margin.left}
              x2={width - margin.right}
              y1={scale.y(tick)}
              y2={scale.y(tick)}
            />
            <text className="dc-label" x={margin.left - 8} y={scale.y(tick) + 4} textAnchor="end">
              {formatDuration(tick)}
            </text>
          </g>
        ))}
        {timeTicks.map((time, index) => (
          <text
            key={time}
            className="dc-label"
            x={scale.x(time)}
            y={height - 6}
            textAnchor={index === 0 ? "start" : index === timeTicks.length - 1 ? "end" : "middle"}
          >
            {formatClock(new Date(time))}
          </text>
        ))}
        {p95Ms !== null && (
          <g>
            <line
              className="dc-p95"
              x1={margin.left}
              x2={width - margin.right}
              y1={scale.y(p95Ms)}
              y2={scale.y(p95Ms)}
            />
            <text className="dc-p95-label" x={margin.left + 6} y={scale.y(p95Ms) - 5}>
              p95 · {formatDuration(p95Ms)}
            </text>
          </g>
        )}
        {picked && (
          <rect
            className="dc-picked"
            x={scale.x(picked.from.getTime())}
            y={scale.y(picked.maxMs)}
            width={Math.max(2, scale.x(picked.to.getTime()) - scale.x(picked.from.getTime()))}
            height={Math.max(2, scale.y(picked.minMs) - scale.y(picked.maxMs))}
            rx={8}
          />
        )}
        {points.map((point) => {
          const kind = point.failed ? "failed" : p95Ms !== null && point.durationMs >= p95Ms ? "slow" : "ok";

          return (
            <circle
              key={point.traceId}
              className={`dc-dot dc-${kind}`}
              cx={scale.x(Date.parse(point.start))}
              cy={scale.y(point.durationMs)}
              r={kind === "ok" ? 2.4 : 3.3}
            />
          );
        })}
        {drag && (
          <rect
            className="dc-drag"
            x={Math.min(drag.x0, drag.x1)}
            y={Math.min(drag.y0, drag.y1)}
            width={Math.abs(drag.x1 - drag.x0)}
            height={Math.abs(drag.y1 - drag.y0)}
            rx={6}
          />
        )}
      </svg>
    </div>
  );
}
