import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import type { AlertListItem } from "../api/types";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import {
  durationChart,
  hostMetrics,
  names,
  overview,
  overviewOf,
  overviewWithDatabase,
  serviceMetrics,
} from "./testMetrics";

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
      alerts: vi.fn(),
      summarizeLogs: vi.fn(),
    },
  };
});

const viewer = {
  id: "1",
  email: "viewer@example.com",
  displayName: "Dana Kim",
  role: "Viewer" as const,
  timeZone: null,
  look: { chosen: null, default: "forecast", mayChoose: true },
};

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

/** An open disk alert on web-01, as the alerts API lists it. */
const diskAlert: AlertListItem = {
  id: "0193a2b4-0000-7000-8000-000000000042",
  rule: "disk-filling",
  environment: "production",
  block: { kind: "host", name: "web-01", part: "/", id: "host:production/web-01" },
  severity: "Caution",
  worst: "Caution",
  value: 9,
  peak: 9,
  peakAt: null,
  threshold: 14,
  windowSeconds: 86400,
  openedAt: "2026-11-10T06:00:00Z",
  updatedAt: "2026-11-10T07:00:00Z",
  resolvedAt: null,
  area: null,
};

beforeEach(() => {
  localStorage.clear();
  vi.mocked(api.alerts).mockResolvedValue({ items: [], next: null });
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.metricsOverview).mockResolvedValue(overview);
  vi.mocked(api.hostMetrics).mockResolvedValue(hostMetrics);
  vi.mocked(api.serviceMetrics).mockResolvedValue(serviceMetrics);
  vi.mocked(api.metricNames).mockResolvedValue(names);
  vi.mocked(api.metricChart).mockResolvedValue(durationChart);
});

describe("MetricsScreen", () => {
  it("shows the usage and a card per host and service", async () => {
    localStorage.setItem("veyra.view", "advanced");
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
    expect(api.metricsOverview).toHaveBeenCalledWith("24h", { search: "", environment: null }, expect.any(AbortSignal));
  });

  it("shows the counts the server makes over the cards", async () => {
    vi.mocked(api.metricsOverview).mockResolvedValue({
      ...overview,
      summary: { ...overview.summary, hosts: 12, fillingDisks: 2, restarting: 4 },
    });

    renderAt("/metrics");

    const hostsFigure = (await screen.findByText("Hosts", { selector: "dt span" })).closest("dt")?.nextSibling;
    expect(hostsFigure).toHaveTextContent("12 2 disks filling");
    expect(screen.getByText("4 restarting")).toBeVisible();
  });

  it("asks the server for the cards the search keeps and lists what it answers", async () => {
    const user = userEvent.setup();
    const webHosts = overview.hosts.filter((host) => host.name.startsWith("web-0"));
    vi.mocked(api.metricsOverview).mockImplementation((_range, choices) =>
      Promise.resolve(
        choices.search === "web-0"
          ? overviewOf({ hosts: webHosts, services: [], databases: [], containers: [] })
          : overview,
      ),
    );
    renderAt("/metrics?range=7d");

    await user.type(await screen.findByRole("searchbox", { name: "Find a host or service" }), "web-0");

    expect(api.metricsOverview).toHaveBeenLastCalledWith(
      "7d",
      { search: "web-0", environment: null },
      expect.any(AbortSignal),
    );
    await waitFor(() => expect(screen.getAllByRole("article")).toHaveLength(webHosts.length));
    expect(screen.queryByRole("region", { name: "Services" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "7d" })).toHaveAttribute("aria-pressed", "true");
  });

  it("leads from a host card to the host's page", async () => {
    const user = userEvent.setup();
    renderAt("/metrics");

    await user.click(await screen.findByRole("link", { name: /^web-01/ }));

    expect(await screen.findByRole("heading", { level: 1, name: "web-01" })).toBeVisible();
    expect(api.hostMetrics).toHaveBeenCalledWith("web-01", "24h", expect.any(AbortSignal));
    const disks = screen.getByRole("region", { name: "Disks" });
    expect(within(disks).getAllByRole("listitem")).toHaveLength(3);
    expect(within(disks).getByRole("meter", { name: "/ in use" })).toHaveAttribute("aria-valuenow", "81");
    expect(within(disks).getByText("full in about 23 days")).toBeVisible();
    expect(screen.getByRole("img", { name: /^CPU and memory: CPU 4\d%/ })).toBeInTheDocument();
    expect(await screen.findByRole("status")).toHaveTextContent("web-01 is fine. No alert is open.");
    expect(screen.queryByRole("region", { name: "Network" })).not.toBeInTheDocument();
    expect(screen.getByText(/in the Advanced view/)).toBeVisible();
  });

  it("puts the way back, the range, and the view toggle in the bar above a host", async () => {
    const user = userEvent.setup();
    renderAt("/metrics/hosts/web-01?range=7d");

    expect(await screen.findByRole("heading", { level: 1, name: "web-01" })).toBeVisible();
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Metrics" })).toHaveAttribute("href", "/metrics?range=7d");
    expect(within(trail).getByRole("link", { name: "Hosts" })).toHaveAttribute(
      "href",
      "/metrics?range=7d#metrics-hosts",
    );
    expect(within(trail).getByText("web-01")).toHaveAttribute("aria-current", "page");
    expect(screen.queryByRole("link", { name: "← Metrics" })).not.toBeInTheDocument();

    const bar = trail.parentElement as HTMLElement;
    expect(within(bar).getByRole("group", { name: "View" })).toBeInTheDocument();
    await user.click(within(bar).getByRole("button", { name: "7d" }));
    expect(api.hostMetrics).toHaveBeenLastCalledWith("web-01", "7d", expect.any(AbortSignal));
  });

  it("scrolls to the group a trail leads back to", async () => {
    const scrolled = vi.fn();
    Element.prototype.scrollIntoView = scrolled;

    vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithDatabase);

    try {
      renderAt("/metrics#metrics-databases");

      await screen.findByRole("heading", { name: "Database servers" });
      await vi.waitFor(() => expect(scrolled).toHaveBeenCalled());
      expect(scrolled.mock.contexts.at(-1)).toBe(document.getElementById("metrics-databases"));
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView;
    }
  });

  it("puts the range and the view toggle in the bar above the overview", async () => {
    renderAt("/metrics");

    const trail = await screen.findByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByText("Metrics")).toHaveAttribute("aria-current", "page");
    const bar = trail.parentElement as HTMLElement;
    expect(within(bar).getByRole("group", { name: "Time range" })).toBeInTheDocument();
    expect(within(bar).getByRole("group", { name: "View" })).toBeInTheDocument();
  });

  it("shows every host panel in the Advanced view, with the fundamentals", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt("/metrics/hosts/web-01");

    expect(await screen.findByRole("region", { name: "Network" })).toBeVisible();
    expect(screen.getByRole("img", { name: /^CPU in use: total 4\d%/ })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Disks" })).toBeVisible();
    expect(screen.getByRole("region", { name: "Now" })).toBeVisible();
    expect(await screen.findByRole("status")).toHaveTextContent("web-01 is fine.");
  });

  it("lists the host's recent errors from its own logs, with a way to all of them", async () => {
    vi.mocked(api.searchLogs).mockResolvedValue({
      items: [
        {
          id: 7,
          timestamp: "2026-11-10T06:58:00Z",
          observedTimestamp: "2026-11-10T06:58:01Z",
          severity: "Error",
          severityText: "ERROR",
          body: "The Veyra agent service terminated unexpectedly.",
          serviceName: "web-01",
          environment: "production",
          traceId: null,
          spanId: null,
          attributes: { "windows.event.id": "7034" },
          resourceAttributes: { "host.name": "web-01" },
        },
      ],
      nextCursor: null,
    });
    renderAt("/metrics/hosts/web-01");

    const issues = await screen.findByRole("region", { name: "Recent errors" });
    expect(issues).toHaveTextContent("The Veyra agent service terminated unexpectedly.");
    expect(within(issues).getByRole("link", { name: /All of the last day/ })).toHaveAttribute(
      "href",
      expect.stringContaining("service=web-01"),
    );
    const [params] = vi.mocked(api.searchLogs).mock.lastCall ?? [new URLSearchParams()];
    expect([params.get("service"), params.get("severity")]).toEqual(["web-01", "Error"]);
  });

  it("says what the open alert of a host is, with a way to it", async () => {
    vi.mocked(api.alerts).mockResolvedValue({ items: [diskAlert], next: null });

    renderAt("/metrics/hosts/web-01");

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(/Disk \/ on web-01/);
    expect(within(status).getByRole("link", { name: "Alert details" })).toHaveAttribute(
      "href",
      `/alerts/${diskAlert.id}`,
    );
  });

  it("lists the containers with a count, each leading to its row on its host", async () => {
    renderAt("/metrics");

    const containers = await screen.findByRole("region", { name: "Containers" });
    expect(within(containers).getAllByRole("article")).toHaveLength(3);
    const worker = within(containers).getByRole("article", { name: "shop-worker" });
    expect(worker).toHaveClass("hot");
    expect(worker).toHaveTextContent("CPU148%");
    expect(worker).toHaveTextContent("Memory93%");
    expect(worker).toHaveTextContent("Restarts3");
    expect(within(worker).getByRole("link")).toHaveAttribute(
      "href",
      "/metrics/hosts/web-01?range=24h#container-shop-worker",
    );
    expect(within(containers).getByRole("article", { name: "redis" })).not.toHaveClass("hot");
    expect(screen.getByText("Containers", { selector: "dt span" }).closest("dt")?.nextSibling).toHaveTextContent(
      "3 1 restarting",
    );
  });

  it("shows the containers of a host with memory of their limit, restarts, uptime, and logs", async () => {
    renderAt("/metrics/hosts/web-01");

    const containers = await screen.findByRole("region", { name: "Containers" });
    const rows = within(containers).getAllByRole("row");
    expect(rows).toHaveLength(3);
    const worker = within(containers).getByRole("row", { name: /shop-worker/ });
    expect(worker).toHaveAttribute("id", "container-shop-worker");
    expect(worker).toHaveTextContent("ghcr.io/example/shop-worker:1.4.2");
    expect(worker).toHaveTextContent("148%");
    expect(within(worker).getByRole("meter", { name: "shop-worker memory of its limit" })).toHaveAttribute(
      "aria-valuenow",
      "93",
    );
    expect(worker).toHaveTextContent("476 MB of 512 MB");
    expect(worker).toHaveTextContent("3 in the last 24 hours");
    expect(worker).toHaveTextContent("7 in all");
    expect(worker).toHaveTextContent("4 min");
    expect(within(worker).getByRole("link", { name: "Logs of shop-worker" })).toHaveAttribute(
      "href",
      "/logs?service=shop-worker",
    );
    expect(within(containers).getByRole("row", { name: /shop-api/ })).toHaveTextContent("3 d 4 h");
  });

  it("keeps a container reached by its link in view while the screen around it still changes", async () => {
    // The app's frame never scrolls or grows; the screen scrolls inside it. So what moves the row is the content
    // changing as the panels above it load, not the page's size.
    const scrolled = vi.fn();
    Element.prototype.scrollIntoView = scrolled;

    try {
      renderAt("/metrics/hosts/web-01#container-shop-worker");
      const worker = await screen.findByRole("row", { name: /shop-worker/ });
      scrolled.mockClear();

      document.querySelector("main")?.prepend(document.createElement("div"));
      await vi.waitFor(() => expect(scrolled).toHaveBeenCalledWith({ block: "center" }));
      expect(scrolled.mock.contexts.at(-1)).toBe(worker);
    } finally {
      // jsdom has no scrollIntoView of its own.
      delete (Element.prototype as Partial<Element>).scrollIntoView;
    }
  });

  it("marks a container with an open alert and leads to it", async () => {
    vi.mocked(api.alerts).mockResolvedValue({
      items: [
        {
          ...diskAlert,
          id: "0193a2b4-0000-7000-8000-000000000077",
          rule: "container-memory",
          block: { kind: "container", name: "shop-worker", part: "web-01", id: "container:production/shop-worker" },
          severity: "Caution",
          value: 0.93,
          threshold: 0.9,
        },
      ],
      next: null,
    });

    renderAt("/metrics/hosts/web-01");

    const containers = await screen.findByRole("region", { name: "Containers" });
    const alert = await within(containers).findByRole("link", { name: /uses 93% of its memory limit/ });
    expect(alert).toHaveAttribute("href", "/alerts/0193a2b4-0000-7000-8000-000000000077");
    expect(within(containers).getByRole("row", { name: /shop-worker/ })).toHaveClass("caution");
  });

  it("shows a service's three figures and slowest routes in the Simple view", async () => {
    renderAt("/metrics/services/payments-gateway?environment=production");

    const slowest = await screen.findByRole("region", { name: "Slowest routes" });
    // The server's slowest routes, in its order, including one too quiet to be among the busiest (VEY-68).
    const rows = within(slowest).getAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent("GET /reports");
    expect(rows[2]).toHaveTextContent("POST /checkout/confirm");
    expect(screen.getByText("Latency p95")).toBeVisible();
    expect(screen.queryByText("Memory")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Latency" })).not.toBeInTheDocument();
  });

  it("shows a service with its routes and a link into its logs", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt("/metrics/services/payments-gateway?environment=production");

    expect(await screen.findByRole("heading", { level: 1, name: "payments-gateway" })).toBeVisible();
    expect(api.serviceMetrics).toHaveBeenCalledWith("payments-gateway", "production", "24h", expect.any(AbortSignal));
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Metrics" })).toHaveAttribute("href", "/metrics?range=24h");
    expect(within(trail).getByText("payments-gateway")).toHaveAttribute("aria-current", "page");
    expect(within(trail.parentElement as HTMLElement).getByRole("group", { name: "Time range" })).toBeInTheDocument();
    const routes = await screen.findByRole("region", { name: "Routes" });
    const rows = within(routes).getAllByRole("row");
    expect(rows[1]).toHaveTextContent("POST /checkout/confirm58 /s520 ms4.2%");
    expect(screen.getByRole("link", { name: "Open logs" })).toHaveAttribute("href", "/logs?service=payments-gateway");
  });

  it("offers the six ranges of the shared time and none pressed for a window of the address", async () => {
    const window = new URLSearchParams({ from: "2026-11-25T09:00:00.000Z", to: "2026-11-26T09:00:00.000Z" });
    renderAt(`/metrics?${window.toString()}`);

    expect(await screen.findByText(/lines are from 25 Nov 09:00 to 26 Nov 09:00./)).toBeVisible();
    expect(api.metricsOverview).toHaveBeenCalledWith(
      "2026-11-25T09:00:00.000Z~2026-11-26T09:00:00.000Z",
      { search: "", environment: null },
      expect.any(AbortSignal),
    );
    const picker = screen.getByRole("group", { name: "Time range" });
    const presets = within(picker).getAllByRole("button");
    expect(presets.map((button) => button.textContent)).toEqual(["15m", "1h", "6h", "24h", "7d", "30d"]);
    expect(presets.filter((button) => button.getAttribute("aria-pressed") === "true")).toHaveLength(0);
    expect(await screen.findByRole("link", { name: /^web-01/ })).toHaveAttribute(
      "href",
      `/metrics/hosts/web-01?${window.toString()}`,
    );
  });

  it("carries a window of the address into a host's trail, and a preset picked replaces it", async () => {
    const user = userEvent.setup();
    const window = new URLSearchParams({ from: "2026-11-25T09:00:00.000Z", to: "2026-11-26T09:00:00.000Z" });
    renderAt(`/metrics/hosts/web-01?${window.toString()}`);

    expect(await screen.findByRole("heading", { level: 1, name: "web-01" })).toBeVisible();
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Metrics" })).toHaveAttribute(
      "href",
      `/metrics?${window.toString()}`,
    );

    await user.click(screen.getByRole("button", { name: "30d" }));

    expect(api.hostMetrics).toHaveBeenLastCalledWith("web-01", "30d", expect.any(AbortSignal));
    expect(within(trail).getByRole("link", { name: "Metrics" })).toHaveAttribute("href", "/metrics?range=30d");
    expect(screen.getByRole("button", { name: "30d" })).toHaveAttribute("aria-pressed", "true");

    await user.click(screen.getByRole("button", { name: "24h" }));

    expect(api.hostMetrics).toHaveBeenLastCalledWith("web-01", "24h", expect.any(AbortSignal));
    expect(screen.getByRole("button", { name: "24h" })).toHaveAttribute("aria-pressed", "true");
  });

  it("says so when a host sent nothing in the range", async () => {
    vi.mocked(api.hostMetrics).mockRejectedValue(new ApiError(404, "Not Found"));

    renderAt("/metrics/hosts/old-box?range=1h");

    expect(await screen.findByText("old-box sent no host metrics within the last hour.")).toBeVisible();
  });

  it("browses metric names and charts the chosen one split by an attribute", async () => {
    const user = userEvent.setup();
    renderAt("/metrics?tab=all");

    const list = await screen.findByRole("list", { name: "Metric names" });
    await user.click(await within(list).findByRole("button", { name: /http.server.request.duration/ }));

    expect(await screen.findByRole("img", { name: /http.server.request.duration p95: checkout-web/ })).toBeVisible();
    expect(api.metricChart).toHaveBeenLastCalledWith(
      { name: "http.server.request.duration", show: null, split: null, service: null, host: null, range: "24h" },
      expect.any(AbortSignal),
    );

    await user.click(screen.getByRole("button", { name: "http.route" }));

    expect(api.metricChart).toHaveBeenLastCalledWith(
      {
        name: "http.server.request.duration",
        show: null,
        split: "http.route",
        service: null,
        host: null,
        range: "24h",
      },
      expect.any(AbortSignal),
    );
  });

  it("keeps the overview to the cards in the Simple view", async () => {
    renderAt("/metrics");

    expect(await screen.findByRole("region", { name: "Hosts" })).toBeVisible();
    expect(screen.queryByText("1,284")).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /All metrics/ })).not.toBeInTheDocument();
  });

  it("explains every figure of the overview and each kind of card", async () => {
    const user = userEvent.setup();
    vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithDatabase);
    localStorage.setItem("veyra.view", "advanced");
    renderAt("/metrics");

    await screen.findByRole("region", { name: "Hosts" });

    for (const name of [
      "Hosts",
      "Services",
      "Databases",
      "Containers",
      "Series",
      "Points",
      "Storage",
      "a host card",
      "a database server card",
      "a container card",
      "a service card",
    ]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeVisible();
    }

    await user.click(screen.getByRole("button", { name: "What Series means" }));

    expect(screen.getByRole("tooltip")).toHaveTextContent(/50,000/);
  });

  it("explains every panel of a host in both views", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchLogs).mockResolvedValue({
      items: [
        {
          id: 7,
          timestamp: "2026-11-10T06:58:00Z",
          observedTimestamp: "2026-11-10T06:58:01Z",
          severity: "Error",
          severityText: "ERROR",
          body: "The disk is full.",
          serviceName: "web-01",
          environment: "production",
          traceId: null,
          spanId: null,
          attributes: {},
          resourceAttributes: { "host.name": "web-01" },
        },
      ],
      nextCursor: null,
    });
    renderAt("/metrics/hosts/web-01");

    await screen.findByRole("region", { name: "Recent errors" });

    for (const name of ["Now", "Recent errors", "CPU and memory", "Disks", "Containers"]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeVisible();
    }

    await user.click(screen.getByRole("button", { name: "What Now means" }));

    expect(screen.getByRole("tooltip")).toHaveTextContent(/last 5 minutes/);

    await user.click(screen.getByRole("button", { name: "Advanced" }));

    for (const name of ["Now", "CPU", "Memory", "Disks", "Network", "Disk I/O", "Containers"]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeVisible();
    }
  });

  it("explains every figure and panel of a service in both views", async () => {
    const user = userEvent.setup();
    renderAt("/metrics/services/payments-gateway?environment=production");

    await screen.findByRole("region", { name: "Slowest routes" });

    for (const name of ["Requests", "Latency p95", "5xx answers", "Slowest routes"]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeVisible();
    }

    await user.click(screen.getByRole("button", { name: "Advanced" }));

    for (const name of [
      "Requests",
      "Latency p95",
      "5xx answers",
      "CPU",
      "Memory",
      "Latency",
      "the Requests chart",
      "Routes",
      "CPU and memory",
      "Runtime",
    ]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeVisible();
    }

    await user.click(screen.getByRole("button", { name: "What 5xx answers means" }));

    expect(screen.getByRole("tooltip")).toHaveTextContent(/5xx/);
  });

  it("explains what the chart of the chosen metric shows", async () => {
    const user = userEvent.setup();
    renderAt("/metrics?tab=all&metric=http.server.request.duration");

    await user.click(await screen.findByRole("button", { name: "What http.server.request.duration means" }));

    expect(screen.getByRole("tooltip")).toHaveTextContent(/95th percentile/);
  });

  it("explains how to send metrics when there are none", async () => {
    const none = overviewOf({ hosts: [], services: [], databases: [], containers: [] });
    vi.mocked(api.metricsOverview).mockResolvedValue({ ...none, summary: { ...none.summary, anySent: false } });

    renderAt("/metrics");

    expect(await screen.findByText("No metrics yet")).toBeVisible();
  });

  it("says nothing matches when the server keeps no card of what was sent", async () => {
    vi.mocked(api.metricsOverview).mockResolvedValue(
      overviewOf({ hosts: [], services: [], databases: [], containers: [] }),
    );

    renderAt("/metrics?search=nobody");

    expect(await screen.findByText("No host or service matches “nobody”.")).toBeVisible();
    expect(screen.queryByText("No metrics yet")).not.toBeInTheDocument();
  });
});
