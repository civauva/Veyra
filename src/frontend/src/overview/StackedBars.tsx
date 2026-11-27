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
}

/**
 * Bars of equal steps, each split into layers, such as log records, spans, and metric points. It stretches to its
 * panel; the legend and a sentence for screen readers state the totals.
 */
export function StackedBars({ layers, title, labels }: StackedBarsProps) {
  const steps = layers[0]?.values.length ?? 0;
  const totals = Array.from({ length: steps }, (_, step) =>
    layers.reduce((sum, layer) => sum + (layer.values[step] ?? 0), 0),
  );
  const top = Math.max(1, ...totals);
  const sums = layers.map((layer) => layer.values.reduce((sum, value) => sum + value, 0));
  const summary = `${title}: ${layers.map((layer, index) => `${layer.name} ${Math.round(sums[index] ?? 0)}`).join(", ")}.`;

  return (
    <figure className="ov-bars">
      <svg viewBox={`0 0 ${Math.max(steps, 1) * 10} 100`} preserveAspectRatio="none" role="img" aria-label={summary}>
        {totals.map((_, step) => {
          let bottom = 100;

          return (
            <g key={step}>
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
