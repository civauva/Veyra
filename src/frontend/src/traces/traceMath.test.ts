import { describe, expect, it } from "vitest";

import type { TraceSpan } from "../api/types";

import { formatDuration, waterfallRows } from "./traceMath";

/** A span as the server sends it, in tree order with its depth; times in milliseconds after 12:00:00. */
function span(id: string, parent: string | null, depth: number, children: number, extra: Partial<TraceSpan> = {}) {
  return {
    spanId: id,
    parentSpanId: parent,
    name: `span ${id}`,
    kind: "Internal",
    start: new Date(Date.UTC(2026, 10, 9, 12, 0, 0, 0)).toISOString(),
    durationMs: 1,
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
    depth,
    children,
    missingParent: false,
    ...extra,
  } satisfies TraceSpan;
}

/** A checkout in the server's tree order (VEY-68), with a span whose parent never arrived at the end. */
const checkout = [
  span("a", null, 0, 3),
  span("b", "a", 1, 0),
  span("c", "a", 1, 2),
  span("e", "c", 2, 0),
  span("d", "c", 2, 1),
  span("f", "d", 3, 0),
  span("g", "a", 1, 0),
  span("x", "missing", 0, 1, { missingParent: true }),
  span("y", "x", 1, 0),
];

describe("waterfallRows", () => {
  it("keeps the server's order, depths, and gaps", () => {
    const rows = waterfallRows(checkout);

    expect(rows.map((row) => `${row.span.spanId}${row.depth}`)).toEqual([
      "a0",
      "b1",
      "c1",
      "e2",
      "d2",
      "f3",
      "g1",
      "x0",
      "y1",
    ]);
    expect(rows.find((row) => row.span.spanId === "c")?.children).toBe(2);
    expect(rows.find((row) => row.span.spanId === "x")).toMatchObject({ depth: 0, missingParent: true });
  });

  it("hides the spans under collapsed spans, and only those", () => {
    const rows = waterfallRows(checkout, new Set(["c", "x"]));

    expect(rows.map((row) => row.span.spanId)).toEqual(["a", "b", "c", "g", "x"]);
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
