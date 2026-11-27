import { type KeyboardEvent, useRef, useState } from "react";

/** One layer of {@link StackedBars}. */
export interface BarLayer {
  /** Its name, in the legend. */
  name: string;
  /** Its colour, a CSS value. */
  color: string;
  /** Its value in each step. */
  values: number[];
}

/** Props for {@link StackedBars}. */
interface StackedBarsProps {
  /** The layers, bottom first. */
  layers: BarLayer[];
  /** What the bars show, for the summary screen readers hear. */
  title: string;
  /** Labels under the first, middle, and last bar. */
  labels: [string, string, string];
  /** The name of each step, such as its clock time or its day, for the tooltip of the bar under the pointer. */
  steps?: string[];
  /** Formats a layer's value in the tooltip; whole numbers when left out. */
  format?: (value: number) => string;
}

/**
 * Bars of equal steps, each split into layers, such as log records, spans, and metric points. It stretches to its
 * panel; the legend and a sentence for screen readers state the totals. Hovering a bar shows its step and each
 * layer's value, the other bars dimmed; the keyboard reaches the same with the arrow keys (VEY-83).
 */
export function StackedBars({ layers, title, labels, steps: names, format = whole }: StackedBarsProps) {
  const [hover, setHover] = useState<number | null>(null);
  const box = useRef<HTMLDivElement>(null);
  const steps = layers[0]?.values.length ?? 0;
  const totals = Array.from({ length: steps }, (_, step) =>
    layers.reduce((sum, layer) => sum + (layer.values[step] ?? 0), 0),
  );
  const top = Math.max(1, ...totals);
  const sums = layers.map((layer) => layer.values.reduce((sum, value) => sum + value, 0));
  const summary = `${title}: ${layers.map((layer, index) => `${layer.name} ${Math.round(sums[index] ?? 0)}`).join(", ")}.`;
  const shown = hover !== null && hover < steps ? hover : null;

  /** The bar under a place across the chart. */
  function under(clientX: number): number | null {
    const rect = box.current?.getBoundingClientRect();

    return rect && steps > 0
      ? Math.min(steps - 1, Math.max(0, Math.floor(((clientX - rect.left) / Math.max(1, rect.width)) * steps)))
      : null;
  }

  function keyed(event: KeyboardEvent<HTMLDivElement>) {
    const at = shown ?? steps - 1;
    const next =
      event.key === "ArrowLeft" ? Math.max(0, at - 1) : event.key === "ArrowRight" ? Math.min(steps - 1, at + 1) : null;

    if (next !== null && steps > 0) {
      event.preventDefault();
      setHover(next);
    } else if (event.key === "Escape") {
      setHover(null);
    }
  }

  return (
    <figure className="ov-bars">
      <div
        ref={box}
        className="bars-plot"
        tabIndex={steps > 0 ? 0 : undefined}
        aria-label={steps > 0 ? `${title}: use the arrow keys to read each bar` : undefined}
        onPointerMove={(event) => setHover(under(event.clientX))}
        onPointerLeave={() => setHover(null)}
        onFocus={() => setHover((current) => current ?? Math.max(0, steps - 1))}
        onBlur={() => setHover(null)}
        onKeyDown={keyed}
      >
        <svg viewBox={`0 0 ${Math.max(steps, 1) * 10} 100`} preserveAspectRatio="none" role="img" aria-label={summary}>
          {totals.map((_, step) => {
            let bottom = 100;

            return (
              <g key={step} opacity={shown === null || shown === step ? 1 : 0.45}>
                {layers.map((layer) => {
                  const height = ((layer.values[step] ?? 0) / top) * 96;
                  bottom -= height;

                  return (
                    <rect
                      key={layer.name}
                      x={step * 10 + 1}
                      y={bottom}
                      width={8}
                      height={height}
                      fill={layer.color}
                      rx={1}
                    />
                  );
                })}
              </g>
            );
          })}
        </svg>
        {shown !== null && (
          <div
            className="bars-tip"
            role="status"
            style={
              shown / steps > 0.6
                ? { right: `${(1 - (shown + 1) / steps) * 100}%` }
                : { left: `${((shown + 1) / steps) * 100}%` }
            }
          >
            <b>{names?.[shown] ?? `Step ${shown + 1}`}</b>
            {layers.map((layer) => (
              <span key={layer.name}>
                <i style={{ background: layer.color }} aria-hidden="true" />
                {layer.name}
                <em>{format(layer.values[shown] ?? 0)}</em>
              </span>
            ))}
          </div>
        )}
      </div>
      <div className="ov-axis" aria-hidden="true">
        {labels.map((label, index) => (
          <span key={index}>{label}</span>
        ))}
      </div>
      <figcaption className="ov-legend">
        {layers.map((layer) => (
          <span key={layer.name}>
            <i style={{ background: layer.color }} aria-hidden="true" />
            {layer.name}
          </span>
        ))}
      </figcaption>
    </figure>
  );
}

/** A count, as a whole number with separators. */
function whole(value: number): string {
  return Math.round(value).toLocaleString("en");
}
