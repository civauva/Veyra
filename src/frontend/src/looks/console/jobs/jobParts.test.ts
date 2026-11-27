import { describe, expect, it } from "vitest";

import { niceSeconds, successFrame } from "./jobParts";

describe("the parts of Console's job screens", () => {
  it("niceSeconds_EachSize_RoundsUpToAReadableTop", () => {
    expect(niceSeconds(0.4)).toBe(1);
    expect(niceSeconds(81.6)).toBe(90);
    expect(niceSeconds(2653)).toBe(2700);
    expect(niceSeconds(5000)).toBe(5400);
    expect(niceSeconds(200_000)).toBe(259_200);
  });

  it("successFrame_Share_FramesOnlyWhatNeedsALook", () => {
    expect(successFrame(0.8)).toBe("bad");
    expect(successFrame(0.97)).toBe("warn");
    expect(successFrame(1)).toBeUndefined();
    expect(successFrame(null)).toBeUndefined();
  });
});
