import { describe, expect, it } from "vitest";

import { formatAgo, formatFull, formatRate, formatSeconds, formatShare, shareTone } from "./metricFormat";

describe("formatSeconds", () => {
  it("reads milliseconds below a second and seconds above", () => {
    expect(formatSeconds(0.34)).toBe("340 ms");
    expect(formatSeconds(0.0042)).toBe("4.2 ms");
    expect(formatSeconds(1.24)).toBe("1.2 s");
    expect(formatSeconds(12.6)).toBe("13 s");
    expect(formatSeconds(null)).toBe("—");
  });
});

describe("formatShare", () => {
  it("keeps one decimal below 10 %", () => {
    expect(formatShare(0.421)).toBe("42%");
    expect(formatShare(0.021)).toBe("2.1%");
    expect(formatShare(null)).toBe("—");
  });

  it("does not round a small share down to nothing", () => {
    expect(formatShare(0.00027)).toBe("<0.1%");
    expect(formatShare(0)).toBe("0%");
  });
});

describe("formatRate", () => {
  it("keeps two significant digits", () => {
    expect(formatRate(118.4)).toBe("118");
    expect(formatRate(2.51)).toBe("2.5");
    expect(formatRate(0.0412)).toBe("0.041");
    expect(formatRate(1234)).toBe("1,234");
  });
});

describe("formatAgo", () => {
  it("says seconds, minutes, or hours", () => {
    const now = new Date("2026-11-10T12:00:00Z");

    expect(formatAgo("2026-11-10T11:59:48Z", now)).toBe("12 s ago");
    expect(formatAgo("2026-11-10T11:56:00Z", now)).toBe("4 min ago");
    expect(formatAgo("2026-11-10T10:00:00Z", now)).toBe("2 h ago");
  });
});

describe("shareTone and formatFull", () => {
  it("colours shares and words disk projections", () => {
    expect([shareTone(0.4), shareTone(0.81), shareTone(0.91)]).toEqual(["ok", "warn", "bad"]);
    expect(formatFull(23.4)).toBe("full in about 23 days");
    expect(formatFull(0.4)).toBe("full within a day");
    expect(formatFull(null)).toBe("steady");
  });
});
