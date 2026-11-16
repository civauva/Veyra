import { describe, expect, it } from "vitest";

import type { TraceSpan } from "../api/types";

import { criticalPath, formatDuration, traceBounds, waterfallRows } from "./traceMath";

/** A span of a checkout that failed in psp-client; times in milliseconds after 12:00:00. */
function span(id: string, parent: string | null, start: number, duration: number, extra: Partial<TraceSpan> = {}) {
  return {
    spanId: id,
    parentSpanId: parent,
    name: `span ${id}`,
    kind: "Internal",
    start: new Date(Date.UTC(2026, 10, 9, 12, 0, 0, start)).toISOString(),
    durationMs: duration,
    status: "Unset",
    statusMessage: null,
    service: "payments",
    environment: "production",
    host: null,
    attributes: {},
    resourceAttributes: {},
    events: [],
    links: [],
    dropped: { attributes: 0, events: 0, links: 0 },
    ...extra,
  } satisfies TraceSpan;
}

const checkout = [
  span("a", null, 0, 1840),
  span("b", "a", 6, 60),
  span("c", "a", 52, 1782),
  span("d", "c", 106, 1612),
  span("e", "c", 100, 14),
  span("f", "d", 116, 1500),
  span("g", "a", 1834, 5),
];

describe("waterfallRows", () => {
  it("orders spans depth first, children by start, with their depth", () => {
    const rows = waterfallRows(checkout);

    expect(rows.map((row) => `${row.span.spanId}${row.depth}`)).toEqual(["a0", "b1", "c1", "e2", "d2", "f3", "g1"]);
    expect(rows.find((row) => row.span.spanId === "c")?.children).toBe(2);
  });

  it("puts spans whose parent has not arrived at the top level and marks them", () => {
    const rows = waterfallRows([...checkout, span("x", "missing", 84, 14), span("y", "x", 88, 8)]);

    const orphan = rows.find((row) => row.span.spanId === "x");
    expect(orphan).toMatchObject({ depth: 0, missingParent: true });
    expect(rows.find((row) => row.span.spanId === "y")).toMatchObject({ depth: 1, missingParent: false });
  });

  it("hides the children of collapsed spans", () => {
    const rows = waterfallRows(checkout, new Set(["c"]));

    expect(rows.map((row) => row.span.spanId)).toEqual(["a", "b", "c", "g"]);
  });
});

describe("criticalPath", () => {
  it("walks back from the end through the children that ran one after another", () => {
    // b and e overlap the work that finished after them, so making them faster would not have helped.
    expect([...criticalPath(checkout)].sort()).toEqual(["a", "c", "d", "f", "g"]);
  });

  it("is empty for no spans", () => {
    expect(criticalPath([]).size).toBe(0);
  });
});

describe("traceBounds", () => {
  it("runs from the first start to the last end", () => {
    const bounds = traceBounds([span("a", null, 10, 100), span("b", "a", 50, 200)]);

    expect(bounds.durationMs).toBe(240);
    expect(new Date(bounds.start).getUTCMilliseconds()).toBe(10);
  });
});

describe("formatDuration", () => {
  it("picks microseconds, milliseconds, or seconds", () => {
    expect(formatDuration(0.85)).toBe("850 µs");
    expect(formatDuration(9)).toBe("9 ms");
    expect(formatDuration(412.4)).toBe("412 ms");
    expect(formatDuration(1840)).toBe("1.84 s");
    expect(formatDuration(65_000)).toBe("65.0 s");
  });
});
