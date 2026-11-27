import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../../api/client";
import { api } from "../../../api/client";
import { durationChart, names, overviewWithDatabase } from "../../../metrics/testMetrics";
import { ConsoleMetrics } from "./ConsoleMetrics";

vi.mock("../../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { metricsOverview: vi.fn(), metricNames: vi.fn(), metricChart: vi.fn() } };
});

/** Shows the address, so a test can check what the screen keeps in it. */
function Address() {
  const location = useLocation();

  return <output aria-label="Address">{location.search}</output>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          path="/metrics"
          element={
            <>
              <ConsoleMetrics />
              <Address />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

const explorer = "/metrics?tab=all&metric=http.server.request.duration&show=p95&split=service.name";

beforeEach(() => {
  localStorage.setItem("veyra.view", "advanced");
  vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithDatabase);
  vi.mocked(api.metricNames).mockResolvedValue(names);
  vi.mocked(api.metricChart).mockResolvedValue(durationChart);
});

describe("ConsoleMetrics", () => {
  it("ConsoleMetrics_Overview_ShowsTheServersFiguresAndEachKindOfBlock", async () => {
    renderAt("/metrics");

    const figures = await screen.findByRole("region", { name: "Figures" });
    expect(within(figures).getByText("Hosts")).toBeVisible();
    expect(screen.getByRole("region", { name: "Hosts" })).toHaveTextContent("web-01");
    expect(screen.getByRole("region", { name: "Services" })).toHaveTextContent("payments-gateway");
    expect(screen.getByRole("region", { name: "Database servers" })).toHaveTextContent("sql-prod-01");
    expect(screen.getByRole("region", { name: "Containers" })).toHaveTextContent("redis");
  });

  it("ConsoleMetrics_OverviewSearch_IsSentToTheServer", async () => {
    const user = userEvent.setup();
    renderAt("/metrics");

    await user.type(await screen.findByRole("searchbox", { name: "Find a host or service" }), "web");

    await waitFor(() =>
      expect(api.metricsOverview).toHaveBeenLastCalledWith(
        "24h",
        { search: "web", environment: null },
        expect.anything(),
      ),
    );
  });

  it("ConsoleMetrics_Explorer_ShowsTheChartAndEachLinesFiguresFromTheServer", async () => {
    renderAt(explorer);

    const series = await screen.findByRole("table", { name: "Series" });
    const row = within(series).getByRole("row", { name: /checkout-web/ });

    expect(within(row).getByText("742 ms")).toBeVisible();
    expect(within(row).getByText("512 ms")).toBeVisible();
    expect(within(row).getByText("1.2 s")).toBeVisible();
    expect(screen.getByRole("columnheader", { name: "service.name" })).toBeVisible();
  });

  it("ConsoleMetrics_ServiceAndHostChosen_AreSentToTheChartRead", async () => {
    const user = userEvent.setup();
    renderAt(explorer);

    const service = await screen.findByRole("combobox", { name: "Service" });
    await user.type(service, "checkout-web{Enter}");
    await user.type(screen.getByRole("combobox", { name: "Host" }), "web-01{Enter}");

    await waitFor(() =>
      expect(api.metricChart).toHaveBeenLastCalledWith(
        expect.objectContaining({ service: "checkout-web", host: "web-01" }),
        expect.anything(),
      ),
    );
    expect(screen.getByRole("status", { name: "Address" })).toHaveTextContent("service=checkout-web");
  });

  it("ConsoleMetrics_ShowPicked_KeepsItInTheAddress", async () => {
    const user = userEvent.setup();
    renderAt(explorer);

    await user.click(await screen.findByRole("button", { name: "p99" }));

    expect(screen.getByRole("status", { name: "Address" })).toHaveTextContent("show=p99");
  });
});
