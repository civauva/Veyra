import { type RefObject, useEffect, useId, useRef, useState } from "react";

import type { ChartLine } from "../api/types";
import { formatClock } from "../logs/format";

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
  /** The chart's height in pixels. Defaults to 170. */
  height?: number;
}

const left = 48;
const right = 8;
const top = 8;
const axis = 20;

/**
 * Lines over time with a value axis, a time axis, and a legend with each line's latest value. It follows the width of
 * its panel, so labels stay readable on a phone. Gaps without data stay gaps instead of dropping to zero.
 */
export function LineChart({ series, format, title, max, height = 170 }: LineChartProps) {
  const box = useRef<HTMLDivElement>(null);
  const width = useWidth(box);
  const gradient = useId();
  const points = series[0]?.line.points ?? [];
  const highest = Math.max(1e-9, ...series.flatMap((one) => one.line.points.map((point) => point.value ?? 0)));
  const scaleTop = max ?? niceTop(highest * 1.1);
  const plotBottom = height - axis;
  const plotRight = width - right;
  const x = (index: number) => left + (points.length > 1 ? (index / (points.length - 1)) * (plotRight - left) : 0);
  const y = (value: number) => plotBottom - (Math.min(value, scaleTop) / scaleTop) * (plotBottom - top);
  const ticks = timeTicks(points.length, width);
  const summary = `${title}: ${series
    .map((one) => `${one.name ?? one.line.label} ${one.line.latest === null ? "no data" : format(one.line.latest)}`)
    .join(", ")}.`;

  return (
    <div className="line-chart" ref={box}>
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
            {share % 0.5 === 0 && (
              <text className="chart-axis" x={left - 6} y={y(scaleTop * share) + 4} textAnchor="end">
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
        {ticks.map((index) => {
          const point = points[index];

          return point ? (
            <text key={index} className="chart-axis" x={x(index)} y={height - 4} textAnchor="middle">
              {label(point.at, points)}
            </text>
          ) : null;
        })}
      </svg>
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

/** The width of an element, following it as it resizes. Falls back to a desktop width where nothing is measured. */
function useWidth(element: RefObject<HTMLDivElement | null>): number {
  const [width, setWidth] = useState(600);

  useEffect(() => {
    const target = element.current;

    if (!target || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      if (entry && entry.contentRect.width > 0) {
        setWidth(Math.round(entry.contentRect.width));
      }
    });
    observer.observe(target);

    return () => observer.disconnect();
  }, [element]);

  return width;
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

/** Clock times within a day; dates when the line spans several days. */
function label(at: string, points: { at: string }[]): string {
  const first = points[0]?.at ?? at;
  const last = points.at(-1)?.at ?? at;
  const days = (new Date(last).getTime() - new Date(first).getTime()) / 86_400_000;

  return days > 2 ? new Date(at).toISOString().slice(5, 10).replace("-", "/") : formatClock(at);
}
