import { describe, expect, it } from "vitest";

import {
  defaultTraceFilters,
  filtersFromSearch,
  filtersToApiParams,
  filtersToSearch,
  isTraceId,
  traceWindow,
} from "./traceFilters";

const now = new Date("2026-11-09T12:00:00Z");

describe("trace filters in the address", () => {
  it("round-trip every filter", () => {
    const filters = {
      ...defaultTraceFilters,
      range: "24h" as const,
      show: "failed" as const,
      services: ["payments", "orders"],
      minMs: 900,
      maxMs: null,
      name: "checkout",
      attributes: [{ key: "http.route", value: "/payments" }],
    };

    expect(filtersFromSearch(filtersToSearch(filters))).toEqual(filters);
  });

  it("leave defaults out of the address and fall back to them for nonsense", () => {
    expect(filtersToSearch(defaultTraceFilters).toString()).toBe("");
    expect(filtersFromSearch(new URLSearchParams("range=2w&show=odd&min=-5"))).toEqual(defaultTraceFilters);
  });
});

describe("traceWindow", () => {
  it("counts the range back from now, or uses the picked window", () => {
    expect(traceWindow({ ...defaultTraceFilters, range: "6h" }, now).from.toISOString()).toBe(
      "2026-11-09T06:00:00.000Z",
    );

    const picked = { ...defaultTraceFilters, from: "2026-11-09T09:10:00.000Z", to: "2026-11-09T09:20:00.000Z" };
    expect(traceWindow(picked, now)).toEqual({ from: new Date(picked.from), to: new Date(picked.to) });
  });
});

describe("filtersToApiParams", () => {
  it("asks the API for exactly what the person chose, in the chosen environment", () => {
    const filters = {
      ...defaultTraceFilters,
      show: "slow" as const,
      services: ["payments"],
      minMs: 900,
      name: "checkout",
      attributes: [{ key: "http.route", value: "/payments" }],
    };

    const params = filtersToApiParams(filters, now, "production", "next-page");

    expect(Object.fromEntries(params)).toEqual({
      from: "2026-11-08T12:00:00.000Z",
      to: "2026-11-09T12:00:00.000Z",
      service: "payments",
      environment: "production",
      slow: "true",
      minDurationMs: "900",
      name: "checkout",
      attribute: "http.route:/payments",
      cursor: "next-page",
    });
  });
});

describe("the order and failed-and-slow (ADR 0029)", () => {
  it("keep the screen's own default order out of the address, and any other in it", () => {
    const worst = { ...defaultTraceFilters, order: "worst" as const };

    expect(filtersToSearch(worst, "worst").toString()).toBe("");
    expect(filtersToSearch(defaultTraceFilters, "worst").toString()).toBe("order=newest");
    expect(filtersFromSearch(new URLSearchParams(""), "worst").order).toBe("worst");
    expect(filtersFromSearch(new URLSearchParams("order=newest"), "worst").order).toBe("newest");
    expect(filtersFromSearch(new URLSearchParams("order=odd")).order).toBe("newest");
  });

  it("read and write failed and slow", () => {
    const both = { ...defaultTraceFilters, show: "failed-slow" as const };

    expect(filtersToSearch(both).toString()).toBe("show=failed-slow");
    expect(filtersFromSearch(filtersToSearch(both))).toEqual(both);
  });

  it("ask the list for worst first, and for failed and slow traces with both filters", () => {
    const filters = { ...defaultTraceFilters, show: "failed-slow" as const, order: "worst" as const };
    const params = filtersToApiParams(filters, now, null);

    expect(params.get("order")).toBe("worst");
    expect(params.get("failed")).toBe("true");
    expect(params.get("slow")).toBe("true");
    expect(filtersToApiParams(defaultTraceFilters, now, null).has("order")).toBe(false);
  });
});

describe("isTraceId", () => {
  it("knows a pasted trace ID from an operation's name", () => {
    expect(isTraceId(" 5B8EFFF798038103D269B633813FC60C ")).toBe(true);
    expect(isTraceId("POST /checkout")).toBe(false);
  });
});
