import type { MetricPreset, MetricRange } from "../api/types";
import { useTimeInBar } from "../app/shellContext";
import { metricRanges } from "./metricRange";

/** Props for {@link RangePicker}. */
interface RangePickerProps {
  /** The range shown: a preset, or a window of the person's own, which presses none of the buttons. */
  value: MetricRange;
  /**
   * Called when the person picks a preset.
   *
   * @param range - The preset.
   */
  onChange(range: MetricPreset): void;
}

/**
 * One-click choices of the shared time range (ADR 0022), as toggle buttons. Nothing is drawn where the shell's top bar
 * chooses the range, so the page has one place to change it.
 */
export function RangePicker({ value, onChange }: RangePickerProps) {
  const inBar = useTimeInBar();

  if (inBar) {
    return null;
  }

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
