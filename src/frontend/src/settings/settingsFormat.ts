import type { Setting } from "../api/types";

/** Seconds in one of each duration unit a setting can be shown in. */
const secondsPer: Record<string, number> = { s: 1, min: 60, h: 3600, days: 86400 };

/**
 * Rounds away float noise, so 0.07 × 100 shows as 7, not 7.000000000000001.
 *
 * @param value - The number to round.
 * @returns The number with at most nine significant digits.
 */
function tidy(value: number): number {
  return Number(value.toPrecision(9));
}

/**
 * Reads a duration as configuration writes it, `d.hh:mm:ss` or `hh:mm:ss`.
 *
 * @param value - The duration.
 * @returns The seconds, or null when it is not a duration.
 */
function parseDuration(value: string): number | null {
  const match = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2}(?:\.\d+)?)$/.exec(value.trim());

  if (!match) {
    return null;
  }

  const [, days, hours, minutes, seconds] = match;

  return Number(days ?? 0) * 86400 + Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds);
}

/**
 * Writes seconds as configuration writes a duration, `d.hh:mm:ss`.
 *
 * @param total - The seconds; whole seconds only.
 * @returns The duration.
 */
function formatDuration(total: number): string {
  const seconds = Math.round(total);
  const days = Math.floor(seconds / 86400);
  const rest = seconds % 86400;
  const pad = (part: number) => String(part).padStart(2, "0");
  const clock = `${pad(Math.floor(rest / 3600))}:${pad(Math.floor((rest % 3600) / 60))}:${pad(rest % 60)}`;

  return days > 0 ? `${days}.${clock}` : clock;
}

/**
 * The value as the field shows it: a ratio as a percentage, a duration in the setting's unit, anything else as it is.
 *
 * @param setting - The setting.
 * @param value - The value in configuration form, or null.
 * @returns The text for the field; empty for no value.
 */
export function toShown(setting: Setting, value: string | null): string {
  if (value === null || value === "") {
    return "";
  }

  if (setting.kind === "Ratio") {
    const ratio = Number(value);

    return Number.isFinite(ratio) ? String(tidy(ratio * 100)) : value;
  }

  if (setting.kind === "Duration") {
    const seconds = parseDuration(value);

    return seconds === null ? value : String(tidy(seconds / (secondsPer[setting.unit ?? "s"] ?? 1)));
  }

  return value;
}

/**
 * The value in configuration form from what the field shows. Text that is not a number stays as typed, so the review
 * says what is wrong with it.
 *
 * @param setting - The setting.
 * @param shown - The text in the field.
 * @returns The value to send.
 */
export function fromShown(setting: Setting, shown: string): string {
  const text = shown.trim();
  const number = Number(text);

  if (text === "" || !Number.isFinite(number)) {
    return text;
  }

  if (setting.kind === "Ratio") {
    return String(tidy(number / 100));
  }

  if (setting.kind === "Duration") {
    return formatDuration(number * (secondsPer[setting.unit ?? "s"] ?? 1));
  }

  return text;
}

/**
 * A value in words, for the review and the history: "5 %", "5 min", "on".
 *
 * @param setting - The setting.
 * @param value - The value in configuration form, or null.
 * @returns The words.
 */
export function describeValue(setting: Setting, value: string | null): string {
  if (setting.kind === "Secret") {
    return "hidden";
  }

  if (value === null || value === "") {
    return setting.optional || setting.kind === "Text" ? "none" : "default";
  }

  if (setting.kind === "Switch") {
    return value === "true" ? "on" : "off";
  }

  const shown = toShown(setting, value);

  return setting.unit ? `${shown} ${setting.unit}` : shown;
}

/**
 * One unsaved change in words, as the save bar lists it: the setting, its value now, and what it becomes. A secret only
 * says it changed.
 *
 * @param setting - The setting.
 * @param value - The new value in configuration form, or null to reset it to its default.
 * @returns For example `Logs, Keep: 14 days to 30 days`.
 */
export function changeWords(setting: Setting, value: string | null): string {
  const name = `${setting.group}, ${setting.label}`;

  if (setting.kind === "Secret") {
    return `${name}: changed`;
  }

  const to = value === null ? `the default, ${describeValue(setting, setting.default)}` : describeValue(setting, value);

  return `${name}: ${describeValue(setting, setting.value)} to ${to}`;
}
