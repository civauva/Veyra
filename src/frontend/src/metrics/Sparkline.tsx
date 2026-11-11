import { useId } from "react";

import type { ChartLine } from "../api/types";

/** Props for {@link Sparkline}. */
interface SparklineProps {
  /** The line. */
  line: ChartLine;
  /** The colour, a CSS value. */
  color: string;
  /** The top of the scale; the line's own highest value when left out. */
  max?: number;
}

const width = 380;
const height = 40;

/**
 * A small line with a soft fill, stretched to the width of its card. Decorative: the card states the numbers.
 */
export function Sparkline({ line, color, max }: SparklineProps) {
  const gradient = useId();
  const values = line.points.map((point) => point.value);
  const top = max ?? Math.max(1e-9, ...values.map((value) => value ?? 0)) * 1.15;
  const step = values.length > 1 ? width / (values.length - 1) : width;
  let path = "";
  let drawing = false;
  let firstX: string | null = null;
  let lastX = "0";

  values.forEach((value, index) => {
    if (value === null) {
      drawing = false;

      return;
    }

    const x = (index * step).toFixed(1);
    const y = height - 2 - (Math.min(value, top) / top) * (height - 4);
    path += `${drawing ? "L" : "M"}${x} ${y.toFixed(1)} `;
    drawing = true;
    firstX ??= x;
    lastX = x;
  });

  // The fill closes under the points that arrived, so a series that started late does not rise from the left edge.
  const fill = `${path.trim()} L${lastX} ${height} L${firstX ?? "0"} ${height} Z`;

  return (
    <svg
      className="sparkline"
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      aria-hidden="true"
      style={{ color }}
    >
      <defs>
        <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stopColor="currentColor" stopOpacity="0.28" />
          <stop offset="1" stopColor="currentColor" stopOpacity="0" />
        </linearGradient>
      </defs>
      {path && (
        <>
          <path d={fill} fill={`url(#${gradient})`} />
          <path d={path.trim()} fill="none" stroke="currentColor" strokeWidth="1.8" vectorEffect="non-scaling-stroke" />
        </>
      )}
    </svg>
  );
}
