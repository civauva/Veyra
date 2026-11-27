import { afterEach, describe, expect, it } from "vitest";

import { rangeParams } from "../api/client";
import { setDisplayZone } from "../time/zone";
import { metricRanges, rangeOf, rangeSearch, rangeWords, withRange } from "./metricRange";

const from = "2026-11-25T09:00:00.000Z";
const to = "2026-11-26T09:00:00.000Z";

afterEach(() => setDisplayZone("UTC"));

describe("metricRanges", () => {
  it("metricRanges_Always_ListsTheSixPresetsOfTheSharedRange", () => {
    expect(metricRanges).toEqual(["15m", "1h", "6h", "24h", "7d", "30d"]);
  });
});

describe("rangeOf", () => {
  it("rangeOf_NoRange_IsTheLast24Hours", () => {
    expect(rangeOf(new URLSearchParams())).toBe("24h");
  });

  it("rangeOf_Preset_IsThatPreset", () => {
    expect(rangeOf(new URLSearchParams("range=15m"))).toBe("15m");
    expect(rangeOf(new URLSearchParams("range=30d"))).toBe("30d");
  });

  it("rangeOf_UnknownPreset_IsTheLast24Hours", () => {
    expect(rangeOf(new URLSearchParams("range=3w"))).toBe("24h");
  });

  it("rangeOf_FromAndTo_IsTheCustomWindow", () => {
    expect(rangeOf(new URLSearchParams({ from, to }))).toBe(`${from}~${to}`);
  });

  it("rangeOf_PresetAndWindow_PrefersThePreset", () => {
    expect(rangeOf(new URLSearchParams({ range: "1h", from, to }))).toBe("1h");
  });

  it("rangeOf_WindowEndingBeforeItStarts_IsTheLast24Hours", () => {
    expect(rangeOf(new URLSearchParams({ from: to, to: from }))).toBe("24h");
  });

  it("rangeOf_UnreadableInstant_IsTheLast24Hours", () => {
    expect(rangeOf(new URLSearchParams({ from: "yesterday", to }))).toBe("24h");
  });
});

describe("rangeParams", () => {
  it("rangeParams_Preset_SendsTheRange", () => {
    expect(rangeParams("6h")).toEqual({ range: "6h" });
  });

  it("rangeParams_CustomWindow_SendsFromAndTo", () => {
    expect(rangeParams(`${from}~${to}`)).toEqual({ from, to });
  });
});

describe("rangeSearch", () => {
  it("rangeSearch_Preset_NamesTheRange", () => {
    expect(rangeSearch("7d")).toBe("range=7d");
  });

  it("rangeSearch_CustomWindow_NamesFromAndTo", () => {
    expect(new URLSearchParams(rangeSearch(`${from}~${to}`))).toEqual(new URLSearchParams({ from, to }));
  });
});

describe("rangeWords", () => {
  it("rangeWords_Presets_ReadAsTheLastPeriod", () => {
    expect(metricRanges.map((range) => rangeWords(range))).toEqual([
      "the last 15 minutes",
      "the last hour",
      "the last 6 hours",
      "the last 24 hours",
      "the last 7 days",
      "the last 30 days",
    ]);
  });

  it("rangeWords_CustomWindow_ReadsFromAndToInTheDisplayZone", () => {
    expect(rangeWords(`${from}~${to}`)).toBe("from 25 Nov 09:00 to 26 Nov 09:00");

    setDisplayZone("Europe/Berlin");

    expect(rangeWords(`${from}~${to}`)).toBe("from 25 Nov 10:00 to 26 Nov 10:00");
  });

  it("rangeWords_Preposition_LeadsAPresetButNotAWindow", () => {
    expect(rangeWords("6h", "over")).toBe("over the last 6 hours");
    expect(rangeWords(`${from}~${to}`, "over")).toBe("from 25 Nov 09:00 to 26 Nov 09:00");
  });
});

describe("withRange", () => {
  it("withRange_Preset_SetsTheRangeAndDropsTheWindow", () => {
    const next = withRange(new URLSearchParams({ tab: "queries", from, to }), "7d");

    expect(next.toString()).toBe("tab=queries&range=7d");
  });

  it("withRange_Default_LeavesTheRangeOut", () => {
    const next = withRange(new URLSearchParams({ range: "1h", search: "web" }), "24h");

    expect(next.toString()).toBe("search=web");
  });
});
