import { describe, expect, it } from "vitest";

import { lookIdOf } from "./looks";

describe("lookIdOf", () => {
  it("lookIdOf_PersonChoseAndMayChoose_IsTheirChoice", () => {
    expect(lookIdOf({ chosen: "console", default: "forecast", mayChoose: true })).toBe("console");
  });

  it("lookIdOf_PersonDidNotChoose_IsTheDefault", () => {
    expect(lookIdOf({ chosen: null, default: "console", mayChoose: true })).toBe("console");
  });

  it("lookIdOf_ChoosingIsOff_IsTheDefaultWhateverTheyChose", () => {
    expect(lookIdOf({ chosen: "console", default: "forecast", mayChoose: false })).toBe("forecast");
  });

  it("lookIdOf_UnknownLook_FallsBackToTheDefaultThenConsole", () => {
    expect(lookIdOf({ chosen: "city", default: "forecast", mayChoose: true })).toBe("forecast");
    expect(lookIdOf({ chosen: null, default: "city", mayChoose: true })).toBe("console");
  });
});
