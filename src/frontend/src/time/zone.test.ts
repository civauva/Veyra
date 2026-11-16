import { afterEach, describe, expect, it } from "vitest";

import { formatClock, formatInstant, formatTime, formatUtc } from "../logs/format";
import {
  cityOf,
  displayZone,
  endOfLocalDay,
  instantOfLocalDay,
  localDay,
  localDayName,
  offsetLabel,
  setDisplayZone,
  zoneLabel,
  zoneParts,
} from "./zone";

const instant = "2026-11-09T07:34:12.418Z";

afterEach(() => {
  setDisplayZone("UTC");
});

describe("the display zone", () => {
  it("is UTC until a zone is set, so a screen outside the shell reads as before", () => {
    expect(displayZone()).toBe("UTC");
    expect(formatTime(instant)).toBe("07:34:12.418");
  });

  it("falls back to UTC for a name no browser knows", () => {
    setDisplayZone("Mars/Olympus_Mons");

    expect(displayZone()).toBe("UTC");
  });

  it("formats times of day, clocks, and instants in the zone", () => {
    setDisplayZone("Europe/Berlin");

    expect(formatTime(instant)).toBe("08:34:12.418");
    expect(formatClock(instant)).toBe("08:34");
    expect(formatInstant(instant)).toBe("2026-11-09 08:34:12.418 CET");
    expect(formatUtc(instant)).toBe("2026-11-09 07:34:12.418 UTC");
  });

  it("names the zone the way people know it, and by offset where there is no common name", () => {
    expect(zoneLabel(new Date(instant))).toBe("UTC");

    setDisplayZone("Europe/Berlin");
    expect(zoneLabel(new Date(instant))).toBe("CET");
    expect(zoneLabel(new Date("2026-07-01T12:00:00Z"))).toBe("CEST");

    setDisplayZone("America/New_York");
    expect(zoneLabel(new Date(instant))).toBe("EST");

    setDisplayZone("Asia/Kolkata");
    expect(zoneLabel(new Date(instant))).toBe("GMT+5:30");
  });

  it("counts days on the zone's calendar", () => {
    // 23:30 UTC is already the next day in Berlin and still the same day in New York.
    setDisplayZone("Europe/Berlin");
    expect(localDay("2026-11-09T23:30:00Z")).toBe("2026-11-10");

    setDisplayZone("America/New_York");
    expect(localDay("2026-11-09T23:30:00Z")).toBe("2026-11-09");
    expect(zoneParts("2026-11-09T23:30:00Z")).toMatchObject({ day: 9, hour: 18, minute: 30 });
  });

  it("names a day Today, Yesterday, or by date, on the zone's calendar", () => {
    setDisplayZone("Asia/Tokyo");
    const now = new Date("2026-11-09T20:00:00Z"); // Nov 10, 05:00 in Tokyo

    expect(localDayName("2026-11-10", now)).toBe("Today");
    expect(localDayName("2026-11-09", now)).toBe("Yesterday");
    expect(localDayName("2026-11-02", now)).toBe("Nov 2");
  });

  it("finds the instant a local day starts, across a daylight saving change", () => {
    setDisplayZone("Europe/Berlin");

    expect(new Date(instantOfLocalDay("2026-11-09")).toISOString()).toBe("2026-11-08T23:00:00.000Z");
    expect(new Date(instantOfLocalDay("2026-07-01")).toISOString()).toBe("2026-06-30T22:00:00.000Z");
  });

  it("ends a local day at its last second, for a key that works through that day", () => {
    setDisplayZone("Europe/Berlin");

    expect(new Date(endOfLocalDay("2026-11-09")).toISOString()).toBe("2026-11-09T22:59:59.000Z");
  });

  it("names a zone by its city and its offset", () => {
    expect(cityOf("America/New_York")).toBe("New York");
    expect(cityOf("UTC")).toBe("UTC");
    expect(offsetLabel("Asia/Kolkata", new Date(instant))).toBe("UTC+05:30");
    expect(offsetLabel("America/New_York", new Date(instant))).toBe("UTC−05:00");
  });
});
