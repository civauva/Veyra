import { describe, expect, it } from "vitest";

import { growthWords, productName, stepWords, timeWords, waitClock } from "./sqlParts";

describe("the words of the Console SQL Server screen", () => {
  it("productName_KnownMajorVersion_NamesTheYear", () => {
    expect(productName("16.0.4135.4", "CU14")).toBe("SQL Server 2022 CU14 (16.0.4135.4)");
    expect(productName("15.0.4365.2", null)).toBe("SQL Server 2019 (15.0.4365.2)");
  });

  it("productName_UnknownMajorVersion_KeepsTheNumber", () => {
    expect(productName("18.0.100.1", null)).toBe("SQL Server (18.0.100.1)");
  });

  it("timeWords_EachSize_UsesTheLargestUnitsThatMatter", () => {
    expect(timeWords(56)).toBe("56 s");
    expect(timeWords(42 * 60)).toBe("42 min");
    expect(timeWords(6 * 3600 + 12 * 60)).toBe("6 h 12 min");
    expect(timeWords(17 * 86_400 + 4 * 3600)).toBe("17 d 4 h");
    expect(timeWords(null)).toBe("—");
  });

  it("stepWords_EachSize_IsAsPreciseAsItMatters", () => {
    expect(stepWords(240)).toBe("240 ms");
    expect(stepWords(1800)).toBe("1.8 s");
    expect(stepWords(74_200)).toBe("74.2 s");
    expect(stepWords(180_000)).toBe("3 min");
  });

  it("waitClock_Seconds_ReadsAsSqlServerShowsWaits", () => {
    expect(waitClock(42 * 60 + 19)).toBe("00:42:19");
  });

  it("growthWords_Sign_IsWritten", () => {
    const format = (bytes: number) => `${bytes} B`;

    expect(growthWords(12, format)).toBe("+12 B");
    expect(growthWords(-3, format)).toBe("−3 B");
    expect(growthWords(null, format)).toBe("—");
  });
});
