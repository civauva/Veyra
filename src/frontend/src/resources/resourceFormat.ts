/**
 * A change against the previous period, as the header strips word it (ADR 0022): a rate in percent, an error ratio in
 * percentage points. The server works it out; this only words it.
 *
 * @param change - The change, as a ratio (0.12 is 12 %, or 12 percentage points), or null without a previous period.
 * @param points - Whether it is a change of a ratio, worded in percentage points.
 * @returns The words, such as "↑ 12% vs the period before", or a dash.
 */
export function changeWords(change: number | null, points = false): string {
  if (change === null) {
    return "no earlier period to compare";
  }

  const size = Math.abs(change * 100);

  if (size < 0.005) {
    return "— steady";
  }

  const arrow = change > 0 ? "↑" : "↓";
  const amount = size < 10 ? size.toFixed(size < 1 ? 2 : 1) : Math.round(size).toString();

  return `${arrow} ${Number(amount)}${points ? " pp" : "%"} vs the period before`;
}

/**
 * Whether a change is bad news: more errors, slower answers. More requests are not.
 *
 * @param change - The change, or null.
 * @param upIsBad - Whether a rise is bad, as for errors and latency.
 * @returns The tone for its words.
 */
export function changeTone(change: number | null, upIsBad: boolean): "bad" | "good" | "flat" {
  if (change === null || Math.abs(change) < 0.00005) {
    return "flat";
  }

  return change > 0 === upIsBad ? "bad" : "good";
}

/**
 * A count in short words: 1,240,000 as 1.24M, 12,400 as 12.4k.
 *
 * @param count - The count, or null.
 * @returns The words, or a dash.
 */
export function shortCount(count: number | null): string {
  if (count === null) {
    return "—";
  }

  if (count >= 1_000_000) {
    return `${Number((count / 1_000_000).toFixed(2))}M`;
  }

  if (count >= 10_000) {
    return `${Number((count / 1_000).toFixed(1))}k`;
  }

  return Math.round(count).toLocaleString("en");
}

/**
 * How long ago something was last seen, in the largest whole unit.
 *
 * @param instant - When, as ISO, or null when never.
 * @param now - The instant to count from, in milliseconds.
 * @returns "3 s", "6 min", "2 h", "4 d", or a dash.
 */
export function seenWords(instant: string | null, now: number): string {
  if (instant === null) {
    return "—";
  }

  const seconds = Math.max(0, Math.round((now - Date.parse(instant)) / 1000));

  if (seconds < 60) {
    return `${seconds} s`;
  }

  if (seconds < 3600) {
    return `${Math.round(seconds / 60)} min`;
  }

  return seconds < 172_800 ? `${Math.round(seconds / 3600)} h` : `${Math.round(seconds / 86_400)} d`;
}
