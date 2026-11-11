import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { durationChart, hostMetrics, names, overview, serviceMetrics, web01 } from "./testMetrics";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      metricsOverview: vi.fn(),
      hostMetrics: vi.fn(),
      serviceMetrics: vi.fn(),
      metricNames: vi.fn(),
      metricChart: vi.fn(),
      searchLogs: vi.fn(),
      summarizeLogs: vi.fn(),
    },
  };
});

const viewer = { id: "1", email: "viewer@example.com", displayName: "Dana Kim", role: "Viewer" as const };

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.metricsOverview).mockResolvedValue(overview);
  vi.mocked(api.hostMetrics).mockResolvedValue(hostMetrics);
  vi.mocked(api.serviceMetrics).mockResolvedValue(serviceMetrics);
  vi.mocked(api.metricNames).mockResolvedValue(names);
  vi.mocked(api.metricChart).mockResolvedValue(durationChart);
});

describe("MetricsScreen", () => {
  it("shows the usage and a card per host and service", async () => {
    renderAt("/metrics");

    const hosts = await screen.findByRole("region", { name: "Hosts" });
    expect(within(hosts).getAllByRole("article")).toHaveLength(3);
    const web = within(hosts).getByRole("article", { name: "web-01" });
    expect(web).toHaveClass("hot");
    expect(web).toHaveTextContent("42%CPU");
    expect(web).toHaveTextContent("Disk / full in about 23 days");
    const payments = within(screen.getByRole("region", { name: "Services" })).getByRole("article", {
      name: "payments-gateway",
    });
    expect(payments).toHaveTextContent("Requests118/s");
    expect(payments).toHaveTextContent("p95340 ms");
    expect(payments).toHaveTextContent("5xx2.1%");
    expect(screen.getByText("1,284")).toBeVisible();
    expect(api.metricsOverview).toHaveBeenCalledWith("6h", expect.any(AbortSignal));
  });

  it("counts the hosts whose disks fill up", async () => {
    vi.mocked(api.metricsOverview).mockResolvedValue({
      ...overview,
      hosts: overview.hosts.map((host) =>
        host.name === "web-02" ? { ...host, fillingDisk: web01.fillingDisk } : host,
      ),
    });

    renderAt("/metrics");

    expect(await screen.findByText("2 disks filling")).toBeVisible();
  });

  it("filters the cards by name and keeps the range in the URL", async () => {
    const user = userEvent.setup();
    renderAt("/metrics?range=24h");

    await user.type(await screen.findByRole("searchbox", { name: "Find a host or service" }), "web-0");

    expect(screen.getAllByRole("article")).toHaveLength(2);
    expect(screen.queryByRole("region", { name: "Services" })).not.toBeInTheDocument();
    expect(api.metricsOverview).toHaveBeenCalledWith("24h", expect.any(AbortSignal));
    expect(screen.getByRole("button", { name: "24h" })).toHaveAttribute("aria-pressed", "true");
  });

  it("leads from a host card to the host's page", async () => {
    const user = userEvent.setup();
    renderAt("/metrics");

    await user.click(await screen.findByRole("link", { name: /web-01/ }));

    expect(await screen.findByRole("heading", { level: 1, name: "web-01" })).toBeVisible();
    expect(api.hostMetrics).toHaveBeenCalledWith("web-01", "6h", expect.any(AbortSignal));
    const disks = screen.getByRole("region", { name: "Disks" });
    expect(within(disks).getAllByRole("listitem")).toHaveLength(3);
    expect(within(disks).getByRole("meter", { name: "/ in use" })).toHaveAttribute("aria-valuenow", "81");
    expect(within(disks).getByText("full in about 23 days")).toBeVisible();
    expect(screen.getByRole("img", { name: /^CPU in use: total 4\d%/ })).toBeInTheDocument();
  });

  it("shows a service with its routes and a link into its logs", async () => {
    renderAt("/metrics/services/payments-gateway?environment=production");

    expect(await screen.findByRole("heading", { level: 1, name: "payments-gateway" })).toBeVisible();
    expect(api.serviceMetrics).toHaveBeenCalledWith("payments-gateway", "production", "6h", expect.any(AbortSignal));
    const routes = await screen.findByRole("region", { name: "Routes" });
    const rows = within(routes).getAllByRole("row");
    expect(rows[1]).toHaveTextContent("POST /checkout/confirm58 /s520 ms4.2%");
    expect(screen.getByRole("link", { name: "Open logs" })).toHaveAttribute("href", "/logs?service=payments-gateway");
  });

  it("says so when a host sent nothing in the range", async () => {
    vi.mocked(api.hostMetrics).mockRejectedValue(new ApiError(404, "Not Found"));

    renderAt("/metrics/hosts/old-box?range=1h");

    expect(await screen.findByText("old-box sent no host metrics within the last 1h.")).toBeVisible();
  });

  it("browses metric names and charts the chosen one split by an attribute", async () => {
    const user = userEvent.setup();
    renderAt("/metrics?tab=all");

    const list = await screen.findByRole("list", { name: "Metric names" });
    await user.click(await within(list).findByRole("button", { name: /http.server.request.duration/ }));

    expect(await screen.findByRole("img", { name: /http.server.request.duration p95: checkout-web/ })).toBeVisible();
    expect(api.metricChart).toHaveBeenLastCalledWith(
      { name: "http.server.request.duration", show: null, split: null, range: "6h" },
      expect.any(AbortSignal),
    );

    await user.click(screen.getByRole("button", { name: "http.route" }));

    expect(api.metricChart).toHaveBeenLastCalledWith(
      { name: "http.server.request.duration", show: null, split: "http.route", range: "6h" },
      expect.any(AbortSignal),
    );
  });

  it("explains how to send metrics when there are none", async () => {
    vi.mocked(api.metricsOverview).mockResolvedValue({ ...overview, hosts: [], services: [] });

    renderAt("/metrics");

    expect(await screen.findByText("No metrics yet")).toBeVisible();
  });
});
