import type { MetricRange } from "../api/types";

/** The ranges every metrics screen offers. */
export const metricRanges: readonly MetricRange[] = ["1h", "6h", "24h", "7d"];

/**
 * Reads a range from the URL, falling back to six hours.
 *
 * @param value - The `range` query parameter.
 * @returns A valid range.
 */
export function rangeFrom(value: string | null): MetricRange {
  return metricRanges.find((range) => range === value) ?? "6h";
}

/** Props for {@link RangePicker}. */
interface RangePickerProps {
  /** The range shown. */
  value: MetricRange;
  /**
   * Called when the person picks another range.
   *
   * @param range - The range.
   */
  onChange(range: MetricRange): void;
}

/** One-click choices of the time range, as toggle buttons. */
export function RangePicker({ value, onChange }: RangePickerProps) {
  return (
    <div className="range-picker" role="group" aria-label="Time range">
      {metricRanges.map((range) => (
        <button
          key={range}
          type="button"
          aria-pressed={range === value}
          className={range === value ? "on" : undefined}
          onClick={() => onChange(range)}
        >
          {range}
        </button>
      ))}
    </div>
  );
}
