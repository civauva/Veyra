import { describe, expect, it } from "vitest";

import { checkoutErrors, checkoutTrace } from "./testTraces";
import { criticalShare, firstException, recordWindow, shortTraceId, traceLevels } from "./traceFigures";

describe("shortTraceId", () => {
  it("shortTraceId_FullId_KeepsTheStartAndTheEnd", () => {
    expect(shortTraceId("8fa21c9d4e5f60718293a4b5c6d7a71c")).toBe("8fa21c9d4e…a71c");
  });

  it("shortTraceId_ShortId_IsKeptWhole", () => {
    expect(shortTraceId("abc123")).toBe("abc123");
  });
});

describe("traceLevels", () => {
  it("traceLevels_SpansOfThreeDepths_IsThree", () => {
    expect(traceLevels(checkoutTrace.spans)).toBe(3);
  });

  it("traceLevels_NoSpans_IsZero", () => {
    expect(traceLevels([])).toBe(0);
  });
});

describe("criticalShare", () => {
  it("criticalShare_PathEndsInTwoSpans_IsTheirShareOfTheTrace", () => {
    const share = criticalShare(checkoutTrace);

    expect(share.spans.map((span) => span.spanId)).toEqual(["0000000000000004", "0000000000000003"]);
    expect(share.share).toBeCloseTo((14 + 1606) / 1840, 5);
  });

  it("criticalShare_OnlyTheRootOnThePath_IsTheWholeTrace", () => {
    const trace = { ...checkoutTrace, summary: { ...checkoutTrace.summary, criticalPath: ["0000000000000001"] } };

    expect(criticalShare(trace)).toEqual({ share: 1, spans: [checkoutTrace.spans[0]] });
  });

  it("criticalShare_NoPath_IsNull", () => {
    const trace = { ...checkoutTrace, summary: { ...checkoutTrace.summary, criticalPath: [] } };

    expect(criticalShare(trace)).toEqual({ share: null, spans: [] });
  });
});

describe("firstException", () => {
  it("firstException_FailedTrace_IsTheCauseSpansException", () => {
    expect(firstException(checkoutTrace)).toEqual({
      type: "System.TimeoutException",
      message: "The PSP did not answer within 1.5 s",
      service: "psp-client",
    });
  });

  it("firstException_ErrorsOnlyView_StillFindsTheCause", () => {
    expect(firstException(checkoutErrors)?.service).toBe("psp-client");
  });

  it("firstException_NothingFailed_IsNull", () => {
    const trace = { ...checkoutTrace, summary: { ...checkoutTrace.summary, cause: null } };

    expect(firstException(trace)).toBeNull();
  });
});

describe("recordWindow", () => {
  it("recordWindow_Trace_IsAMinuteBeforeAndAfter", () => {
    expect(recordWindow(checkoutTrace.summary)).toEqual({
      from: "2026-11-09T11:59:00.000Z",
      to: "2026-11-09T12:01:01.840Z",
    });
  });
});
