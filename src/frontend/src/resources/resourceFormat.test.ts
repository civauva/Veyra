import { describe, expect, it } from "vitest";

import { changeTone, changeWords, seenWords, shortCount } from "./resourceFormat";

describe("resourceFormat", () => {
  it("changeWords_RiseOfARate_IsPercent", () => {
    expect(changeWords(0.12)).toBe("↑ 12% vs the period before");
    expect(changeWords(-0.034)).toBe("↓ 3.4% vs the period before");
  });

  it("changeWords_ChangeOfARatio_IsPercentagePoints", () => {
    expect(changeWords(0.0012, true)).toBe("↑ 0.12 pp vs the period before");
  });

  it("changeWords_NoPeriodBeforeOrNoChange_SaysSo", () => {
    expect(changeWords(null)).toBe("no earlier period to compare");
    expect(changeWords(0)).toBe("— steady");
  });

  it("changeTone_MoreErrorsAreBad_MoreRequestsAreGood", () => {
    expect(changeTone(0.01, true)).toBe("bad");
    expect(changeTone(0.12, false)).toBe("good");
    expect(changeTone(null, true)).toBe("flat");
  });

  it("shortCount_LargeCounts_AreShort", () => {
    expect(shortCount(1_240_000)).toBe("1.24M");
    expect(shortCount(12_400)).toBe("12.4k");
    expect(shortCount(812)).toBe("812");
    expect(shortCount(null)).toBe("—");
  });

  it("seenWords_Ages_UseTheLargestUnit", () => {
    const now = Date.parse("2026-11-26T09:00:00Z");

    expect(seenWords("2026-11-26T08:59:57Z", now)).toBe("3 s");
    expect(seenWords("2026-11-26T08:54:00Z", now)).toBe("6 min");
    expect(seenWords("2026-11-26T07:00:00Z", now)).toBe("2 h");
    expect(seenWords("2026-11-20T09:00:00Z", now)).toBe("6 d");
    expect(seenWords(null, now)).toBe("—");
  });
});
