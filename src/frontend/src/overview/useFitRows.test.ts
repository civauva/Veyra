import { describe, expect, it } from "vitest";

import { fitRows } from "./useFitRows";

describe("fitRows", () => {
  it("fitRows_RoomBelowTheRows_AsksForTheRowsThatFit", () => {
    expect(fitRows({ shown: 8, rowHeight: 32, spare: 200, asked: 8, least: 8, most: 50 })).toBe(14);
  });

  it("fitRows_NoRoom_KeepsWhatItAsked", () => {
    expect(fitRows({ shown: 8, rowHeight: 32, spare: 0, asked: 8, least: 8, most: 50 })).toBe(8);
  });

  it("fitRows_FewerRowsThanAsked_NeverAsksForFewer", () => {
    expect(fitRows({ shown: 3, rowHeight: 32, spare: 100, asked: 20, least: 8, most: 50 })).toBe(20);
  });

  it("fitRows_TallTile_StopsAtTheMost", () => {
    expect(fitRows({ shown: 8, rowHeight: 32, spare: 5000, asked: 8, least: 8, most: 50 })).toBe(50);
  });

  it("fitRows_NothingShownYet_KeepsWhatItAsked", () => {
    expect(fitRows({ shown: 0, rowHeight: 0, spare: 400, asked: 8, least: 8, most: 50 })).toBe(8);
  });
});
