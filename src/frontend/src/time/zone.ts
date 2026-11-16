/*
 * The time zone every screen shows times in (VEY-22). The shell sets it from the person's choice on the Account page,
 * the browser's zone until they make one, or UTC when they switch to it in the account menu. Until the shell sets it,
 * as in component tests of a single screen, it is UTC, the way Veyra showed every time before.
 *
 * The formatters read the zone from here instead of taking it as an argument, so the hundred places that show a time
 * stay as short as they were. The shell renders the screens again when the zone changes.
 */

/** The parts of an instant on a zone's clock and calendar. */
export interface ZoneParts {
  /** The year, for example 2026. */
  year: number;
  /** The month, 1 to 12. */
  month: number;
  /** The day of the month. */
  day: number;
  /** The hour, 0 to 23. */
  hour: number;
  /** The minute. */
  minute: number;
  /** The second. */
  second: number;
  /** The millisecond. */
  millisecond: number;
}

let current = "UTC";

const partFormats = new Map<string, Intl.DateTimeFormat>();
const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * The zone the browser runs in, the default until a person picks one.
 *
 * @returns An IANA name, such as `Europe/Berlin`; UTC when the browser does not say.
 */
export function browserZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

/**
 * Whether the browser knows a zone by this name.
 *
 * @param zone - An IANA name.
 * @returns True when times can be shown in it.
 */
export function isKnownZone(zone: string): boolean {
  try {
    new Intl.DateTimeFormat("en-US", { timeZone: zone });

    return true;
  } catch {
    return false;
  }
}

/**
 * Sets the zone every formatter uses. An unknown name falls back to UTC rather than breaking every screen.
 *
 * @param zone - An IANA name.
 */
export function setDisplayZone(zone: string): void {
  current = isKnownZone(zone) ? zone : "UTC";
}

/**
 * The zone every formatter uses now.
 *
 * @returns An IANA name, or `UTC`.
 */
export function displayZone(): string {
  return current;
}

function partsFormat(zone: string): Intl.DateTimeFormat {
  let format = partFormats.get(zone);

  if (!format) {
    format = new Intl.DateTimeFormat("en-US", {
      timeZone: zone,
      hourCycle: "h23",
      year: "numeric",
      month: "numeric",
      day: "numeric",
      hour: "numeric",
      minute: "numeric",
      second: "numeric",
    });
    partFormats.set(zone, format);
  }

  return format;
}

/**
 * An instant on the clock and calendar of a zone.
 *
 * @param instant - The instant, as a date, an ISO 8601 string, or milliseconds.
 * @param zone - The zone; the display zone when not given.
 * @returns Its parts.
 */
export function zoneParts(instant: Date | string | number, zone: string = current): ZoneParts {
  const date = new Date(instant);
  const parts: Record<string, number> = {};

  for (const part of partsFormat(zone).formatToParts(date)) {
    if (part.type !== "literal") {
      parts[part.type] = Number(part.value);
    }
  }

  return {
    year: parts.year ?? 0,
    month: parts.month ?? 1,
    day: parts.day ?? 1,
    hour: parts.hour ?? 0,
    minute: parts.minute ?? 0,
    second: parts.second ?? 0,
    millisecond: date.getUTCMilliseconds(),
  };
}

/**
 * How far a zone's clock is ahead of UTC at an instant.
 *
 * @param instant - The instant.
 * @param zone - The zone; the display zone when not given.
 * @returns The offset in minutes, for example 60 for Berlin in winter.
 */
export function offsetMinutes(instant: Date | string | number, zone: string = current): number {
  const time = new Date(instant).getTime();
  const parts = zoneParts(time, zone);
  const asUtc = Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second);

  return Math.round((asUtc - (time - (time % 1000))) / 60_000);
}

/**
 * The short name of the display zone at an instant, as people know it: `CET`, `CEST`, `EST`, or `UTC`. Where no name
 * is common the offset stands in, for example `GMT+5:30`.
 *
 * @param at - The instant, since the name changes with daylight saving. Defaults to now.
 * @returns The name.
 */
export function zoneLabel(at: Date = new Date()): string {
  if (current === "UTC") {
    return "UTC";
  }

  const name = (locale: string) =>
    new Intl.DateTimeFormat(locale, { timeZone: current, timeZoneName: "short" })
      .formatToParts(at)
      .find((part) => part.type === "timeZoneName")?.value ?? "";
  const american = name("en-US");

  // American English names American zones; British English names most of Europe's (CET, CEST, EET).
  if (american.startsWith("GMT")) {
    const british = name("en-GB");

    return british && !british.startsWith("GMT") ? british : american;
  }

  return american;
}

/**
 * The calendar day of an instant in the display zone.
 *
 * @param instant - The instant.
 * @returns The day as `YYYY-MM-DD`.
 */
export function localDay(instant: Date | string | number): string {
  const { year, month, day } = zoneParts(instant);

  return `${year}-${pad(month)}-${pad(day)}`;
}

/**
 * A short date in the display zone, for example `Nov 8`.
 *
 * @param instant - The instant.
 * @returns The date.
 */
export function shortDate(instant: Date | string | number): string {
  const { month, day } = zoneParts(instant);

  return `${months[month - 1] ?? ""} ${day}`;
}

/**
 * A calendar day as a heading: `Today`, `Yesterday`, or `Nov 8`, on the display zone's calendar.
 *
 * @param day - The day as `YYYY-MM-DD`.
 * @param now - The current instant.
 * @returns The name.
 */
export function localDayName(day: string, now: Date): string {
  const today = localDay(now);
  const yesterday = shiftDay(today, -1);

  if (day === today) {
    return "Today";
  }

  if (day === yesterday) {
    return "Yesterday";
  }

  const [, month, date] = day.split("-").map(Number);

  return `${months[(month ?? 1) - 1] ?? ""} ${date ?? ""}`;
}

/**
 * A calendar day moved by whole days, for example the day before.
 *
 * @param day - The day as `YYYY-MM-DD`.
 * @param days - How many days to move; negative moves back.
 * @returns The day as `YYYY-MM-DD`.
 */
export function shiftDay(day: string, days: number): string {
  return new Date(Date.parse(`${day}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);
}

/**
 * The instant a calendar day starts in the display zone: its midnight, or the first time that exists when daylight
 * saving skips midnight.
 *
 * @param day - The day as `YYYY-MM-DD`.
 * @returns Milliseconds since the epoch.
 */
export function instantOfLocalDay(day: string): number {
  const midnight = Date.parse(`${day}T00:00:00Z`);
  // The offset at a guess, then at the instant it gives, settles within two steps across any change.
  let instant = midnight - offsetMinutes(midnight) * 60_000;
  instant = midnight - offsetMinutes(instant) * 60_000;

  return instant;
}

/**
 * The last second of a calendar day in the display zone, for a date that means "through that day", such as the
 * expiry of an ingestion key.
 *
 * @param day - The day as `YYYY-MM-DD`.
 * @returns Milliseconds since the epoch.
 */
export function endOfLocalDay(day: string): number {
  return instantOfLocalDay(shiftDay(day, 1)) - 1000;
}

/**
 * The city of an IANA zone, for a short label: `Europe/Berlin` is `Berlin`, `America/New_York` is `New York`.
 *
 * @param zone - The IANA name.
 * @returns The city, or the name itself when it has no city.
 */
export function cityOf(zone: string): string {
  const city = zone.split("/").pop() ?? zone;

  return city.replace(/_/g, " ");
}

/**
 * A zone's offset from UTC at an instant, as the zone list shows it: `UTC+01:00`, `UTC−05:00`.
 *
 * @param zone - The IANA name.
 * @param at - The instant. Defaults to now.
 * @returns The offset.
 */
export function offsetLabel(zone: string, at: Date = new Date()): string {
  const minutes = offsetMinutes(at, zone);
  const sign = minutes < 0 ? "−" : "+";
  const size = Math.abs(minutes);

  return `UTC${sign}${pad(Math.floor(size / 60))}:${pad(size % 60)}`;
}

function pad(value: number, width = 2): string {
  return String(value).padStart(width, "0");
}
