import { describe, expect, it } from "vitest";

import type { HomeAlert } from "../api/types";
import { conditionTitle, formatAge, formatBytes, formatLoad, formatRatio, severityWord } from "./homeFormat";
import { narrative } from "./narrative";
import { clearSnapshot, stormSnapshot } from "./testSnapshots";

const [, stormCaution] = stormSnapshot.alerts as [HomeAlert, HomeAlert];

describe("formatRatio", () => {
  it("shows small ratios with two decimals and larger ones with one", () => {
    expect(formatRatio(0.0002)).toBe("0.02%");
    expect(formatRatio(0.004)).toBe("0.4%");
    expect(formatRatio(0.058)).toBe("5.8%");
    expect(formatRatio(0.216)).toBe("21.6%");
    expect(formatRatio(0)).toBe("0%");
  });

  it("shows a dash without records", () => {
    expect(formatRatio(null)).toBe("—");
  });
});

describe("conditionTitle", () => {
  it("names the weather and where it is", () => {
    expect(conditionTitle("Storm", "Services")).toBe("Storm over Services");
    expect(conditionTitle("Showers", "Services")).toBe("Showers over Services");
    expect(conditionTitle("Clear", null)).toBe("Clear skies");
  });
});

describe("narrative", () => {
  it("names the worst service, the showery ones, and how many are dry", () => {
    expect(narrative(stormSnapshot)).toBe(
      "payments-gateway fails 21.6% of the time since 06:58. billing-worker is showery. 6 of 8 services are dry.",
    );
  });

  it("says when every service is dry", () => {
    expect(narrative(clearSnapshot)).toBe("No alerts are open. All 8 services are dry.");
  });

  it("says when no logs arrived", () => {
    expect(narrative({ ...clearSnapshot, blocks: [] })).toBe("No logs arrived in the last hour.");
  });

  it("joins several showery services", () => {
    const caution = (service: string, n: number): HomeAlert => ({
      ...stormCaution,
      id: service,
      service,
      name: service,
      n,
    });
    const snapshot = {
      ...stormSnapshot,
      alerts: [caution("orders", 1), caution("billing", 2), caution("search", 3)],
    };

    expect(narrative(snapshot)).toMatch(/^orders, billing and search are showery\./);
  });
});

describe("formatAge", () => {
  const now = new Date("2026-11-07T07:34:00Z");

  it("uses minutes, then hours, then days", () => {
    expect(formatAge("2026-11-07T06:58:00Z", now)).toBe("36 min");
    expect(formatAge("2026-11-07T05:20:00Z", now)).toBe("2 h");
    expect(formatAge("2026-11-03T07:34:00Z", now)).toBe("4 d");
    expect(formatAge("2026-11-07T07:33:40Z", now)).toBe("1 min");
  });
});

describe("formatBytes", () => {
  it("picks a readable unit", () => {
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(340 * 1024 * 1024)).toBe("340 MB");
    expect(formatBytes(1.2 * 1024 * 1024 * 1024)).toBe("1.2 GB");
    expect(formatBytes(20 * 1024 * 1024 * 1024)).toBe("20 GB");
  });
});

describe("formatLoad", () => {
  it("shows the expected load as a multiple of an average hour", () => {
    expect(formatLoad(1.43)).toBe("1.4×");
    expect(formatLoad(null)).toBe("learning");
  });
});

describe("rule text", () => {
  it("uses the shared words for severities", () => {
    expect(severityWord("Warning")).toBe("warning");
    expect(severityWord("Caution")).toBe("caution");
  });
});
