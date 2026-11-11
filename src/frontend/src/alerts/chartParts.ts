import { type RefObject, useEffect, useState } from "react";

/*
 * What the alert charts share: following their panel's width, a time axis with round times, and lines through
 * points that may have gaps.
 */

/**
 * The width of an element, following it as it resizes. Falls back to a desktop width where nothing is measured.
 *
 * @param element - The element to measure.
 * @returns Its width in pixels.
 */
export function useWidth(element: RefObject<HTMLDivElement | null>): number {
  const [width, setWidth] = useState(900);

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

/**
 * Round times along a time axis, about one per 140 pixels: minutes and hours for short periods, whole days for
 * weeks.
 *
 * @param from - The start, in milliseconds.
 * @param to - The end, in milliseconds.
 * @param plotWidth - The width of the plot in pixels.
 * @returns The ticks, in milliseconds.
 */
export function timeTicks(from: number, to: number, plotWidth: number): number[] {
  const wanted = Math.max(3, Math.floor(plotWidth / 140));
  const steps = [5, 10, 15, 30, 60, 120, 180, 360, 720, 1440, 2880, 4320, 10080].map((minutes) => minutes * 60_000);
  const step = steps.find((candidate) => (to - from) / candidate <= wanted) ?? steps[steps.length - 1] ?? 60_000;
  const ticks: number[] = [];

  for (let tick = Math.ceil(from / step) * step; tick <= to; tick += step) {
    ticks.push(tick);
  }

  return ticks;
}

/**
 * A line through every point with a value; a point without one breaks it.
 *
 * @param points - The points, oldest first.
 * @param x - Where a point's time is.
 * @param y - Where a value is.
 * @returns The SVG path.
 */
export function gappedPath(
  points: { at: string; value: number | null }[],
  x: (at: string) => number,
  y: (value: number) => number,
): string {
  let path = "";
  let drawing = false;

  for (const point of points) {
    if (point.value === null) {
      drawing = false;
      continue;
    }

    path += `${drawing ? "L" : "M"}${x(point.at).toFixed(1)} ${y(point.value).toFixed(1)} `;
    drawing = true;
  }

  return path.trim();
}

/**
 * Where a label beside a vertical marker goes: to its right, or to its left when it would run off the plot.
 *
 * @param at - The marker's x.
 * @param plotRight - The right edge of the plot.
 * @param room - The width the label needs.
 * @returns The label's x and anchor.
 */
export function besideLine(at: number, plotRight: number, room = 110): { x: number; textAnchor: "start" | "end" } {
  return at + 6 + room > plotRight ? { x: at - 6, textAnchor: "end" } : { x: at + 6, textAnchor: "start" };
}

/**
 * A scale top above the highest value with round grid steps, for charts whose values have no natural top.
 *
 * @param highest - The highest value to show.
 * @returns The top and its grid step.
 */
export function niceScale(highest: number): { top: number; step: number } {
  const magnitude = 10 ** Math.floor(Math.log10(Math.max(highest, 1e-9)));

  for (const factor of [1, 2, 2.5, 5, 10]) {
    const step = factor * magnitude;

    if (highest / step <= 5) {
      return { top: Math.ceil(highest / step) * step, step };
    }
  }

  return { top: 10 * magnitude, step: 2 * magnitude };
}
