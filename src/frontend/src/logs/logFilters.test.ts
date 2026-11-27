import { describe, expect, it } from "vitest";

import { defaultFilters, filtersFromUrl, filtersToApiParams, filtersToUrl, type LogFilters } from "./logFilters";

const now = new Date("2026-11-09T12:00:00.000Z");

describe("filtersToApiParams", () => {
  it("turns the time range into from and to, ending now", () => {
    const params = filtersToApiParams({ ...defaultFilters, range: "15m" }, now);

    expect(params.get("from")).toBe("2026-11-09T11:45:00.000Z");
    expect(params.get("to")).toBe("2026-11-09T12:00:00.000Z");
  });

  it("repeats service and attribute parameters, one per value", () => {
    const filters: LogFilters = {
      ...defaultFilters,
      services: ["payments", "orders"],
      attributes: [{ key: "http.route", value: "/pay" }],
    };

    const params = filtersToApiParams(filters, now);

    expect(params.getAll("service")).toEqual(["payments", "orders"]);
    expect(params.getAll("attribute")).toEqual(["http.route:/pay"]);
  });

  it("leaves out empty filters", () => {
    const params = filtersToApiParams(defaultFilters, now);

    expect([...params.keys()].sort()).toEqual(["from", "limit", "to"]);
  });

  it("adds the cursor and the minimum severity when given", () => {
    const params = filtersToApiParams({ ...defaultFilters, minimumSeverity: "Warning" }, now, "abc");

    expect(params.get("severity")).toBe("Warning");
    expect(params.get("cursor")).toBe("abc");
  });

  it("searches one minute on either side of a record when around is set, instead of the range", () => {
    const params = filtersToApiParams({ ...defaultFilters, around: "2026-11-09T10:00:00.000Z" }, now);

    expect(params.get("from")).toBe("2026-11-09T09:59:00.000Z");
    expect(params.get("to")).toBe("2026-11-09T10:01:00.000Z");
  });

  it("trims the search text", () => {
    const params = filtersToApiParams({ ...defaultFilters, text: "  declined  " }, now);

    expect(params.get("text")).toBe("declined");
  });
});

describe("filtersToUrl and filtersFromUrl", () => {
  it("round-trip every filter, so a link reopens the same view", () => {
    const filters: LogFilters = {
      range: "6h",
      window: null,
      minimumSeverity: "Error",
      services: ["payments"],
      text: "timeout",
      query: "host:web-01",
      attributes: [{ key: "http.route", value: "/a:b" }],
      traceId: "5b8efff798038103d269b633813fc60c",
      around: "2026-11-09T10:00:00.000Z",
    };

    expect(filtersFromUrl(new URLSearchParams(filtersToUrl(filters)))).toEqual(filters);
  });

  it("falls back to the defaults for unknown or missing values", () => {
    const filters = filtersFromUrl(new URLSearchParams("range=3w&severity=Loud&attribute=broken&around=yesterday"));

    expect(filters).toEqual(defaultFilters);
  });
});

describe("a window of one's own (ADR 0022)", () => {
  it("filtersFromUrl_FromAndTo_SearchThatWindowAndKeepItInTheAddress", () => {
    const filters = filtersFromUrl(new URLSearchParams("from=2026-11-20T00:00:00.000Z&to=2026-11-21T00:00:00.000Z"));

    expect(filters.window).toEqual({ from: "2026-11-20T00:00:00.000Z", to: "2026-11-21T00:00:00.000Z" });
    expect(filtersToApiParams(filters, new Date("2026-11-26T09:00:00Z")).get("from")).toBe("2026-11-20T00:00:00.000Z");
    expect(filtersToUrl(filters)).toContain("from=2026-11-20T00%3A00%3A00.000Z");
  });

  it("filtersFromUrl_PresetBesideAWindow_ThePresetWins", () => {
    expect(
      filtersFromUrl(new URLSearchParams("range=7d&from=2026-11-20T00:00:00Z&to=2026-11-21T00:00:00Z")).window,
    ).toBeNull();
  });
});

describe("the query of the query bar (ADR 0024)", () => {
  it("filtersFromUrl_Q_ReadsTheQuery", () => {
    expect(filtersFromUrl(new URLSearchParams("q=service%3Aorders")).query).toBe("service:orders");
  });

  it("filtersToApiParams_Query_SendsItAsQ", () => {
    const params = filtersToApiParams({ ...defaultFilters, query: ' level>=warning "timed out" ' }, now);

    expect(params.get("q")).toBe('level>=warning "timed out"');
  });

  it("filtersToUrl_EmptyQuery_LeavesItOut", () => {
    expect(filtersToUrl({ ...defaultFilters, query: "  " })).not.toContain("q=");
  });
});
