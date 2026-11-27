import { describe, expect, it } from "vitest";

import { instantOfWall, timeFrom, timeSearch, wallOf, windowOf } from "./time";

const now = Date.parse("2026-11-26T09:00:00Z");

describe("timeFrom", () => {
  it("timeFrom_Preset_IsThatPreset", () => {
    expect(timeFrom(new URLSearchParams("range=7d"))).toEqual({ preset: "7d", from: null, to: null });
  });

  it("timeFrom_CustomWindow_KeepsFromAndTo", () => {
    const params = new URLSearchParams("from=2026-11-25T09:00:00.000Z&to=2026-11-26T09:00:00.000Z");

    expect(timeFrom(params)).toEqual({
      preset: null,
      from: "2026-11-25T09:00:00.000Z",
      to: "2026-11-26T09:00:00.000Z",
    });
  });

  it("timeFrom_NothingOrNonsense_IsTheLast24Hours", () => {
    expect(timeFrom(new URLSearchParams("")).preset).toBe("24h");
    expect(timeFrom(new URLSearchParams("range=3w")).preset).toBe("24h");
    expect(timeFrom(new URLSearchParams("from=yesterday&to=now")).preset).toBe("24h");
  });
});

describe("windowOf", () => {
  it("windowOf_Preset_EndsNow", () => {
    expect(windowOf({ preset: "6h", from: null, to: null }, now)).toEqual({
      from: "2026-11-26T03:00:00.000Z",
      to: "2026-11-26T09:00:00.000Z",
    });
  });

  it("windowOf_Custom_IsAsChosen", () => {
    const time = { preset: null, from: "2026-11-20T00:00:00.000Z", to: "2026-11-21T00:00:00.000Z" };

    expect(windowOf(time, now)).toEqual({ from: time.from, to: time.to });
  });
});

describe("carrying the range", () => {
  it("timeSearch_Preset_WritesRange", () => {
    expect(timeSearch({ preset: "1h", from: null, to: null })).toBe("range=1h");
  });
});

describe("wall times in a zone", () => {
  it("instantOfWall_WallTimeInBerlin_IsTheInstant", () => {
    expect(instantOfWall("2026-11-25T09:00", "Europe/Berlin")).toBe("2026-11-25T08:00:00.000Z");
    expect(instantOfWall("2026-07-01T09:00", "Europe/Berlin")).toBe("2026-07-01T07:00:00.000Z");
  });

  it("wallOf_Instant_IsTheWallTimeInTheZone", () => {
    expect(wallOf("2026-11-25T08:00:00.000Z", "Europe/Berlin")).toBe("2026-11-25T09:00");
    expect(wallOf("2026-11-25T08:00:00.000Z", "UTC")).toBe("2026-11-25T08:00");
  });

  it("instantOfWall_Nonsense_IsNull", () => {
    expect(instantOfWall("", "UTC")).toBeNull();
  });
});
