import { type RefObject, useEffect, useId, useRef, useState } from "react";

import type { ChartLine } from "../api/types";
import { formatClock } from "../logs/format";
import { localDay } from "../time/zone";

/** One line to draw, with its colour. */
export interface ChartSeries {
  /** The line. */
  line: ChartLine;
  /** Its colour, a CSS value. */
  color: string;
  /** Its name in the legend; the line's label when left out. */
  name?: string;
  /** Drawn dashed. */
  dashed?: boolean;
  /** Filled below. */
  filled?: boolean;
}

/** Props for {@link LineChart}. */
interface LineChartProps {
  /** The lines; they share the time axis. */
  series: ChartSeries[];
  /** Formats values for the axis, the legend, and the summary. */
  format(value: number): string;
  /** What the chart shows, for the summary screen readers hear. */
  title: string;
  /** The top of the scale; the highest value when left out. */
  max?: number;
  /**
   * The least height of the plot in pixels, 170 when left out. Where its tile is taller, as beside a taller tile in the
   * same row, the plot grows to fill it.
   */
  height?: number;
}

const left = 48;
const right = 8;
const top = 8;
const axis = 20;

/**
 * Lines over time with a value axis, a time axis, and a legend with each line's latest value. It follows the width and
 * the height of its tile, so labels stay readable on a phone and the plot fills a tall tile; the labels stay inside the
 * plot's box. Gaps without data stay gaps instead of dropping to zero, and a range without data says so.
 *
 * Hovering the plot shows the moment under the pointer, with each line's value there and a mark on each line (VEY-83).
 * The keyboard reaches the same: the plot takes the focus, and the arrow keys, Home, and End move through the points.
 */
export function LineChart({ series, format, title, max, height: least = 170 }: LineChartProps) {
  const plot = useRef<HTMLDivElement>(null);
  const size = useSize(plot);
  const width = size.width;
  const height = Math.max(least, size.height);
  const gradient = useId();
  const points = series[0]?.line.points ?? [];
  const values = series.flatMap((one) =>
    one.line.points.flatMap((point) => (point.value === null ? [] : [point.value])),
  );
  const highest = Math.max(0, ...values);
  const scaleTop = max ?? (highest > 0 ? niceTop(highest * 1.1) : 1);
  const labelled = valueLabels(scaleTop, format, max === undefined && highest === 0);
  const plotBottom = height - axis;
  const plotRight = width - right;
  const x = (index: number) => left + (points.length > 1 ? (index / (points.length - 1)) * (plotRight - left) : 0);
  const y = (value: number) => plotBottom - (Math.min(value, scaleTop) / scaleTop) * (plotBottom - top);
  const ticks = timeTicks(points.length, width);
  const [hover, setHover] = useState<number | null>(null);
  const shown = hover !== null && points[hover] ? hover : null;

  /** The point nearest a place across the plot. */
  function nearest(clientX: number): number | null {
    const box = plot.current?.getBoundingClientRect();

    if (!box || points.length === 0) {
      return null;
    }

    const share = (clientX - box.left - left) / Math.max(1, plotRight - left);

    return Math.min(points.length - 1, Math.max(0, Math.round(share * (points.length - 1))));
  }

  function keyed(event: React.KeyboardEvent) {
    const last = points.length - 1;
    const at = shown ?? last;
    const next =
      event.key === "ArrowLeft"
        ? Math.max(0, at - 1)
        : event.key === "ArrowRight"
          ? Math.min(last, at + 1)
          : event.key === "Home"
            ? 0
            : event.key === "End"
              ? last
              : null;

    if (next !== null && last >= 0) {
      event.preventDefault();
      setHover(next);
    } else if (event.key === "Escape") {
      setHover(null);
    }
  }
  const summary = `${title}: ${series
    .map((one) => `${one.name ?? one.line.label} ${one.line.latest === null ? "no data" : format(one.line.latest)}`)
    .join(", ")}.`;

  return (
    <div className="line-chart">
      <div
        className="line-chart-plot"
        ref={plot}
        style={{ minHeight: least }}
        tabIndex={values.length > 0 ? 0 : undefined}
        aria-label={values.length > 0 ? `${title}: use the arrow keys to read each moment` : undefined}
        onPointerMove={(event) => values.length > 0 && setHover(nearest(event.clientX))}
        onPointerLeave={() => setHover(null)}
        onFocus={() => setHover((current) => current ?? Math.max(0, points.length - 1))}
        onBlur={() => setHover(null)}
        onKeyDown={keyed}
      >
        <svg width={width} height={height} role="img" aria-label={summary}>
          <defs>
            <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0" stopColor="currentColor" stopOpacity="0.25" />
              <stop offset="1" stopColor="currentColor" stopOpacity="0" />
            </linearGradient>
          </defs>
          {[0, 0.25, 0.5, 0.75, 1].map((share) => (
            <g key={share}>
              <line className="chart-grid" x1={left} x2={plotRight} y1={y(scaleTop * share)} y2={y(scaleTop * share)} />
              {labelled.has(share) && (
                <text className="chart-axis value" x={left - 6} y={y(scaleTop * share) + 4} textAnchor="end">
                  {share === 0 ? "0" : format(scaleTop * share)}
                </text>
              )}
            </g>
          ))}
          {series.map((one, index) => {
            const path = linePath(one.line, x, y);

            return (
              <g key={`${one.line.label}-${index}`} style={{ color: one.color }}>
                {one.filled && path && <path d={areaPath(one.line, x, y, plotBottom)} fill={`url(#${gradient})`} />}
                {path && (
                  <path
                    d={path}
                    fill="none"
                    stroke="currentColor"
                    strokeWidth={one.dashed ? 1.5 : 2}
                    strokeDasharray={one.dashed ? "5 4" : undefined}
                    strokeLinejoin="round"
                  />
                )}
              </g>
            );
          })}
          {ticks.map((index, place) => {
            const point = points[index];

            return point ? (
              <text
                key={index}
                className="chart-axis time"
                x={x(index)}
                y={height - 4}
                textAnchor={place === 0 ? "start" : place === ticks.length - 1 ? "end" : "middle"}
              >
                {label(point.at, points)}
              </text>
            ) : null;
          })}
          {shown !== null && (
            <g className="chart-hover" aria-hidden="true">
              <line x1={x(shown)} x2={x(shown)} y1={top} y2={plotBottom} />
              {series.map((one, index) => {
                const value = one.line.points[shown]?.value ?? null;

                return value === null ? null : (
                  <circle key={index} cx={x(shown)} cy={y(value)} r={3.5} style={{ fill: one.color }} />
                );
              })}
            </g>
          )}
        </svg>
        {values.length === 0 && <p className="chart-empty">No data in this range</p>}
        {shown !== null && (
          <div
            className="chart-tip"
            role="status"
            style={
              x(shown) > width - 190
                ? { right: Math.max(4, width - x(shown) + 12) }
                : { left: Math.min(width - 4, x(shown) + 12) }
            }
          >
            <b>{moment(points[shown]?.at ?? "", points)}</b>
            {series.map((one, index) => {
              const value = one.line.points[shown]?.value ?? null;

              return (
                <span key={`${one.line.label}-${index}`}>
                  <i style={{ background: one.color }} aria-hidden="true" />
                  {one.name ?? one.line.label}
                  <em>{value === null ? "no data" : format(value)}</em>
                </span>
              );
            })}
          </div>
        )}
      </div>
      <ul className="chart-legend">
        {series.map((one, index) => (
          <li key={`${one.line.label}-${index}`}>
            <i style={{ background: one.color }} aria-hidden="true" />
            {one.name ?? one.line.label} <b>{one.line.latest === null ? "—" : format(one.line.latest)}</b>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * The size of the plot's box, following it as it resizes. The drawing sits on top of the box rather than in its flow,
 * so the box takes its size from the tile alone and drawing at that size never grows it. Falls back to a desktop width
 * and no extra height where nothing is measured.
 */
function useSize(element: RefObject<HTMLDivElement | null>): { width: number; height: number } {
  const [size, setSize] = useState({ width: 600, height: 0 });

  useEffect(() => {
    const target = element.current;

    if (!target || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      if (entry && entry.contentRect.width > 0) {
        setSize({ width: Math.round(entry.contentRect.width), height: Math.floor(entry.contentRect.height) });
      }
    });
    observer.observe(target);

    return () => observer.disconnect();
  }, [element]);

  return size;
}

/**
 * The shares of the scale that carry a value label: the bottom, the middle, and the top, leaving out any that would
 * read the same as one below it, as small counts rounded by the format would. A chart without values labels only the
 * zero, since any other figure would be made up.
 */
function valueLabels(top: number, format: (value: number) => string, empty: boolean): Set<number> {
  const shown = new Set<number>([0]);

  if (empty) {
    return shown;
  }

  const words = new Set<string>(["0"]);

  for (const share of [0.5, 1]) {
    const word = format(top * share);

    if (!words.has(word)) {
      shown.add(share);
      words.add(word);
    }
  }

  return shown;
}

/** A round number above the highest value, so the top label reads well. */
function niceTop(value: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const step = [1, 2, 2.5, 5, 10].find((candidate) => candidate * magnitude >= value) ?? 10;

  return step * magnitude;
}

function linePath(line: ChartLine, x: (index: number) => number, y: (value: number) => number): string {
  let path = "";
  let drawing = false;

  line.points.forEach((point, index) => {
    if (point.value === null) {
      drawing = false;

      return;
    }

    path += `${drawing ? "L" : "M"}${x(index).toFixed(1)} ${y(point.value).toFixed(1)} `;
    drawing = true;
  });

  return path.trim();
}

function areaPath(line: ChartLine, x: (index: number) => number, y: (value: number) => number, bottom: number): string {
  const present = line.points.map((point, index) => ({ index, value: point.value })).filter((p) => p.value !== null);
  const first = present[0];
  const last = present.at(-1);

  if (!first || !last) {
    return "";
  }

  const top = present.map((point) => `L${x(point.index).toFixed(1)} ${y(point.value ?? 0).toFixed(1)}`).join(" ");

  return `M${x(first.index).toFixed(1)} ${bottom} ${top} L${x(last.index).toFixed(1)} ${bottom} Z`;
}

/** About one time label per 110 pixels, evenly spaced. */
function timeTicks(count: number, width: number): number[] {
  if (count < 2) {
    return [];
  }

  const wanted = Math.max(2, Math.min(7, Math.floor((width - left) / 110)));

  return Array.from({ length: wanted }, (_, index) => Math.round((index / (wanted - 1)) * (count - 1)));
}

/** The moment of a point in a tooltip: its clock time, with its day when the line spans more than one. */
function moment(at: string, points: { at: string }[]): string {
  const first = points[0]?.at ?? at;
  const last = points.at(-1)?.at ?? at;

  return localDay(first) === localDay(last)
    ? formatClock(at)
    : `${localDay(at).slice(5).replace("-", "/")} ${formatClock(at)}`;
}

/** Clock times within a day; dates when the line spans several days. */
function label(at: string, points: { at: string }[]): string {
  const first = points[0]?.at ?? at;
  const last = points.at(-1)?.at ?? at;
  const days = (new Date(last).getTime() - new Date(first).getTime()) / 86_400_000;

  return days > 2 ? localDay(at).slice(5).replace("-", "/") : formatClock(at);
}
