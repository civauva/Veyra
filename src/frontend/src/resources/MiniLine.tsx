/** Props for {@link MiniLine}. */
interface MiniLineProps {
  /** The values across the window, in equal steps; null where nothing is known. */
  values: (number | null)[];
  /** Its colour: the accent, or red for a row in trouble. */
  tone: "ok" | "bad";
  /** A class of the screen that places it, such as a tile's foot. */
  className?: string;
}

/** A small line of a row's values across the window. Decorative: the row states its figures. */
export function MiniLine({ values, tone, className }: MiniLineProps) {
  const known = values.filter((value): value is number => value !== null);

  if (values.length < 2 || known.length < 2) {
    return <span className="rs-none">—</span>;
  }

  const width = 120;
  const height = 22;
  const top = Math.max(...known) || 1;
  const points = values
    .map((value, index) => (value === null ? null : ([index, value] as const)))
    .filter((point): point is readonly [number, number] => point !== null)
    .map(
      ([index, value]) =>
        `${((index / (values.length - 1)) * width).toFixed(1)},${(height - 2 - (value / top) * (height - 4)).toFixed(1)}`,
    )
    .join(" ");

  return (
    <svg
      className={["rs-line", tone, className].filter(Boolean).join(" ")}
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      aria-hidden="true"
    >
      <polyline points={points} fill="none" stroke="currentColor" strokeWidth="1.5" />
    </svg>
  );
}
