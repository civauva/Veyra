import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { durations, tracesPage } from "./testTraces";
import { defaultTraceFilters } from "./traceFilters";
import { useTraceList } from "./useTraceList";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: { listTraces: vi.fn(), traceDurations: vi.fn(), traceServices: vi.fn(), traceSummary: vi.fn() },
  };
});

const summary = { traces: 120, previousTraces: 100, failed: 3, slow: 6, p50Ms: 62, p95Ms: 900, services: 2 };

beforeEach(() => {
  vi.mocked(api.listTraces).mockResolvedValue(tracesPage);
  vi.mocked(api.traceDurations).mockResolvedValue(durations);
  vi.mocked(api.traceServices).mockResolvedValue([]);
  vi.mocked(api.traceSummary).mockResolvedValue(summary);
});

describe("useTraceList", () => {
  it("useTraceList_WithoutOptions_AsksForNoSummaryAndServicesByTraces", async () => {
    const { result } = renderHook(() => useTraceList(defaultTraceFilters, null));

    await waitFor(() => expect(result.current.items).not.toBeNull());

    expect(api.traceSummary).not.toHaveBeenCalled();
    expect(vi.mocked(api.traceServices).mock.calls[0]?.[0].has("order")).toBe(false);
    expect(result.current.summary).toBeNull();
  });

  it("useTraceList_ConsoleOptions_AsksForTheSummaryAndTheSlowestServices", async () => {
    const filters = { ...defaultTraceFilters, order: "worst" as const, services: ["checkout-web"] };
    const { result } = renderHook(() => useTraceList(filters, "production", { summary: true, slowest: true }));

    await waitFor(() => expect(result.current.summary).toEqual(summary));

    const list = vi.mocked(api.listTraces).mock.calls[0]?.[0];
    const services = vi.mocked(api.traceServices).mock.calls[0]?.[0];
    const figures = vi.mocked(api.traceSummary).mock.calls[0]?.[0];

    expect(list?.get("order")).toBe("worst");
    expect(services?.get("order")).toBe("slowest");
    expect(services?.has("service")).toBe(false);
    expect(figures?.get("service")).toBe("checkout-web");
    expect(figures?.has("order")).toBe(false);
    expect(figures?.get("environment")).toBe("production");
  });
});
