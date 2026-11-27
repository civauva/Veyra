import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { setDisplayZone } from "../time/zone";
import { alertFigures, alertState, blockLink } from "./alertFigures";
import { openDiskFilling, openWarning, resolvedLatency } from "./testAlerts";

beforeEach(() => setDisplayZone("UTC"));

afterEach(() => setDisplayZone("UTC"));

describe("alertFigures", () => {
  it("alertFigures_DiskAlert_TellsUseGrowthFullInAndThreshold", () => {
    const figures = alertFigures(openDiskFilling);

    expect(figures.map((figure) => figure.label)).toEqual(["Used now", "Growing", "Full in", "Threshold"]);
    expect(figures[0]).toMatchObject({ value: "86%", detail: "80 GB of 93 GB", tone: "warn" });
    expect(figures[1]?.value).toBe("1.5 pt a day");
    expect(figures[2]).toMatchObject({ value: "9 days", detail: "Nov 19, at this pace" });
    expect(figures[3]?.value).toBe("14 days");
  });

  it("alertFigures_ErrorRateAlert_TellsNowPeakThresholdAndOpenFor", () => {
    const figures = alertFigures(openWarning);

    expect(figures.map((figure) => figure.label)).toEqual(["Now", "Peak", "Threshold", "Open for"]);
    expect(figures[0]).toMatchObject({ value: "21.6%", tone: "bad" });
    expect(figures[1]).toMatchObject({ value: "24.1%", detail: "at 07:12" });
    expect(figures[3]).toMatchObject({ value: "36 min", detail: "since 06:58" });
  });

  it("alertFigures_ResolvedAlert_TellsTheEndAndHowLongItLasted", () => {
    const figures = alertFigures(resolvedLatency);

    expect(figures.map((figure) => figure.label)).toEqual(["At the end", "Peak", "Threshold", "Lasted"]);
    expect(figures[0]?.tone).toBeUndefined();
    expect(figures[3]).toMatchObject({ value: "25 min", detail: "21:40–22:05" });
  });

  it("alertFigures_EveryFigure_ExplainsItself", () => {
    for (const alert of [openDiskFilling, openWarning, resolvedLatency]) {
      expect(alertFigures(alert).every((figure) => figure.help.length > 20)).toBe(true);
    }
  });
});

describe("alertState", () => {
  it("alertState_OpenAndResolved_AreWordedWithTheirTone", () => {
    expect(alertState(openWarning)).toEqual({ words: "Warning · open", tone: "bad" });
    expect(alertState(openDiskFilling)).toEqual({ words: "Caution · open", tone: "warn" });
    expect(alertState(resolvedLatency)).toEqual({ words: "Resolved", tone: "off" });
  });
});

describe("blockLink", () => {
  it("blockLink_EachKind_LeadsToItsScreen", () => {
    expect(blockLink(openWarning.block)).toEqual({
      to: "/metrics/services/payments-gateway",
      label: "Open payments-gateway",
    });
    expect(blockLink(openDiskFilling.block)).toEqual({ to: "/metrics/hosts/web-01", label: "Open host web-01" });
  });
});
