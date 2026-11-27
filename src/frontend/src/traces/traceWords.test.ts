import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { setDisplayZone } from "../time/zone";
import { tracesPage } from "./testTraces";
import {
  clockSeconds,
  durationChoices,
  failedShare,
  orderChoices,
  showChoices,
  traceChange,
  traceStatus,
} from "./traceWords";

beforeEach(() => setDisplayZone("UTC"));

afterEach(() => setDisplayZone("UTC"));

const summary = { traces: 106, previousTraces: 100, failed: 3, slow: 9, p50Ms: 62, p95Ms: 214, services: 9 };

describe("traceChange", () => {
  it("traceChange_EarlierPeriod_IsTheShareOfChange", () => {
    expect(traceChange(summary)).toBeCloseTo(0.06, 5);
  });

  it("traceChange_NothingBefore_IsNull", () => {
    expect(traceChange({ ...summary, previousTraces: null })).toBeNull();
    expect(traceChange({ ...summary, previousTraces: 0 })).toBeNull();
  });
});

describe("failedShare", () => {
  it("failedShare_FewOfMany_KeepsTwoDecimals", () => {
    expect(failedShare(3, 18_402)).toBe("0.02%");
    expect(failedShare(8, 96)).toBe("8.33%");
    expect(failedShare(0, 2550)).toBe("0.00%");
  });

  it("failedShare_NoTraces_IsADash", () => {
    expect(failedShare(0, 0)).toBe("—");
  });
});

describe("traceStatus", () => {
  it("traceStatus_EachTrace_IsFailedSlowOrOk", () => {
    const [failed, ok] = tracesPage.items;

    if (!failed || !ok) {
      throw new Error("The fixture lists two traces.");
    }

    expect(traceStatus(failed, 900)).toBe("failed");
    expect(traceStatus({ ...ok, durationMs: 950 }, 900)).toBe("slow");
    expect(traceStatus(ok, 900)).toBe("ok");
    expect(traceStatus(ok, null)).toBe("ok");
  });
});

describe("clockSeconds", () => {
  it("clockSeconds_Instant_IsTheTimeOfDayWithSeconds", () => {
    expect(clockSeconds("2026-11-09T14:32:08.421Z")).toBe("14:32:08");
  });
});

describe("the chips' choices", () => {
  it("offer every show, both orders, and duration bands", () => {
    expect(showChoices.map(([value]) => value)).toEqual(["all", "failed", "slow", "failed-slow"]);
    expect(orderChoices.map(([value]) => value)).toEqual(["worst", "newest"]);
    expect(durationChoices[0]).toEqual([null, "Any"]);
    expect(durationChoices.map(([value]) => value)).toContain(300);
  });
});
