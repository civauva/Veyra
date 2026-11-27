import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { durationChart, names } from "./testMetrics";
import { metricShows, useMetricExplorer } from "./useMetricExplorer";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { metricNames: vi.fn(), metricChart: vi.fn() } };
});

function at(path: string) {
  return ({ children }: { children: ReactNode }) => <MemoryRouter initialEntries={[path]}>{children}</MemoryRouter>;
}

beforeEach(() => {
  vi.mocked(api.metricNames).mockResolvedValue(names);
  vi.mocked(api.metricChart).mockResolvedValue(durationChart);
});

describe("useMetricExplorer", () => {
  it("useMetricExplorer_ServiceAndHostInTheAddress_AreSentToTheChartRead", async () => {
    const { result } = renderHook(() => useMetricExplorer("24h"), {
      wrapper: at(
        "/metrics?tab=all&metric=http.server.request.duration&show=p95&split=service.name&service=checkout&host=web-01",
      ),
    });

    await waitFor(() => expect(result.current.chart.data).not.toBeNull());

    expect(api.metricChart).toHaveBeenCalledWith(
      {
        name: "http.server.request.duration",
        show: "p95",
        split: "service.name",
        service: "checkout",
        host: "web-01",
        range: "24h",
      },
      expect.anything(),
    );
    expect(result.current.service).toBe("checkout");
    expect(result.current.host).toBe("web-01");
  });

  it("useMetricExplorer_NoMetricChosen_ChartsNothing", async () => {
    const { result } = renderHook(() => useMetricExplorer("1h"), { wrapper: at("/metrics?tab=all&q=http") });

    await waitFor(() => expect(result.current.names.data).not.toBeNull());

    expect(api.metricNames).toHaveBeenCalledWith("http", "1h", expect.anything());
    expect(api.metricChart).not.toHaveBeenCalled();
    expect(result.current.chart.data).toBeNull();
  });
});

describe("metricShows", () => {
  it("metricShows_EachKind_OffersWhatItCanShow", () => {
    expect(metricShows.Gauge).toEqual(["value"]);
    expect(metricShows.Sum).toEqual(["value", "rate"]);
    expect(metricShows.Histogram).toEqual(["p50", "p95", "p99", "rate"]);
  });
});
