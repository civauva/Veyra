import { describe, expect, it } from "vitest";

import { daysOfWindow, summarySentence } from "./alertQuery";
import { historySummary } from "./testAlertHistory";

const now = Date.parse("2026-11-26T12:00:00Z");

/** A window counted back from now by so many hours. */
function hoursBack(hours: number) {
  return { from: new Date(now - hours * 3_600_000).toISOString(), to: new Date(now).toISOString() };
}

describe("the alerts period of a look whose top bar chooses the range", () => {
  it("daysOfWindow_ShorterThanADay_IsOneDay", () => {
    expect(daysOfWindow(hoursBack(0.25), now)).toBe(1);
    expect(daysOfWindow(hoursBack(24), now)).toBe(1);
  });

  it("daysOfWindow_WholeDays_AreThoseDays", () => {
    expect(daysOfWindow(hoursBack(7 * 24), now)).toBe(7);
    expect(daysOfWindow(hoursBack(30 * 24), now)).toBe(30);
  });

  it("daysOfWindow_ReadAMomentAfterTheWindowWasTaken_KeepsItsWholeDays", () => {
    // The window is worked out first and the days a moment later, on a busy machine seconds later.
    expect(daysOfWindow(hoursBack(7 * 24), now + 5_000)).toBe(7);
  });

  it("daysOfWindow_LongerThanTheHistoryKept_IsTheHistoryKept", () => {
    expect(daysOfWindow(hoursBack(200 * 24), now)).toBe(90);
  });

  it("daysOfWindow_InvalidStart_IsThirtyDays", () => {
    expect(daysOfWindow({ from: "never", to: new Date(now).toISOString() }, now)).toBe(30);
  });

  it("summarySentence_OneDay_SaysTheLastDay", () => {
    expect(summarySentence({ ...historySummary, total: 0 }, 1)).toContain("No alerts in the last day.");
  });
});
