import type { HistogramBucket } from "../api/types";
import type { WeatherKind } from "./WeatherIcon";

/** The error ratio from which a past hour is drawn as a storm: the warning threshold of the built-in rule. */
export const stormRatio = 0.05;

/** The error ratio from which a past hour is drawn as showers: the caution threshold of the built-in rule. */
export const showerRatio = 0.01;

/**
 * The weather of a past hour: storm or showers by the default thresholds of the built-in error-rate rule, otherwise
 * sun by day and moon by night, in UTC. Used by the hourly strip and the 24-hour chart, so both agree.
 *
 * @param bucket - The hour.
 * @param ratio - Its share of error records, or null without records.
 * @returns The picture.
 */
export function observedIcon(bucket: HistogramBucket, ratio: number | null): WeatherKind {
  if (ratio !== null && ratio >= stormRatio) {
    return "storm";
  }

  if (ratio !== null && ratio >= showerRatio) {
    return "shower";
  }

  const hour = new Date(bucket.start).getUTCHours();

  return hour >= 6 && hour < 20 ? "sun" : "moon";
}
