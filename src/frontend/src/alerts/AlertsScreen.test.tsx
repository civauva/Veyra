import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { AlertListItem, AlertSummary } from "../api/types";
import { App } from "../app/App";
import { stormSnapshot } from "../home/testSnapshots";
import { SessionProvider } from "../session/SessionProvider";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      alerts: vi.fn(),
      alertSummary: vi.fn(),
    },
  };
});

const viewer = { id: "1", email: "viewer@example.com", displayName: "Dana Kim", role: "Viewer" as const };

const days = Array.from({ length: 30 }, (_, index) => ({
  date: new Date(Date.parse("2026-10-12T00:00:00Z") + index * 86_400_000).toISOString().slice(0, 10),
  count: index === 29 ? 2 : index === 4 ? 1 : 0,
  worst: index === 29 ? ("Warning" as const) : index === 4 ? ("Caution" as const) : null,
}));

const summary: AlertSummary = {
  from: "2026-10-12T00:00:00Z",
  to: "2026-11-10T07:34:00Z",
  open: 2,
  openWarnings: 1,
  total: 3,
  warnings: 1,
  resolved: 1,
  medianSeconds: 18 * 60,
  rules: [
    { rule: "error-rate", count: 2, enabled: true, cautionThreshold: 0.01, warningThreshold: 0.05, windowSeconds: 300 },
    { rule: "latency-p95", count: 0, enabled: true, cautionThreshold: 1, warningThreshold: 3, windowSeconds: 300 },
    { rule: "disk-space", count: 0, enabled: false, cautionThreshold: 0.9, warningThreshold: 0.95, windowSeconds: 300 },
    { rule: "disk-filling", count: 1, enabled: true, cautionThreshold: 14, warningThreshold: 3, windowSeconds: 86_400 },
  ],
  days,
};

const disk: AlertListItem = {
  id: "alert-disk",
  rule: "disk-filling",
  environment: "production",
  block: { kind: "host", name: "web-01", part: "/", id: "host:production/web-01" },
  severity: "Caution",
  worst: "Caution",
  value: 9.2,
  peak: 9.2,
  peakAt: "2026-11-10T07:30:00Z",
  threshold: 14,
  windowSeconds: 86_400,
  openedAt: "2026-11-10T03:20:00Z",
  updatedAt: "2026-11-10T07:34:00Z",
  resolvedAt: null,
};

const errors: AlertListItem = {
  ...disk,
  id: "alert-errors",
  rule: "error-rate",
  block: { kind: "service", name: "payments-gateway", part: null, id: "service:production/payments-gateway" },
  severity: "Warning",
  worst: "Warning",
  value: 0.216,
  peak: 0.241,
  peakAt: "2026-11-10T07:12:00Z",
  threshold: 0.05,
  windowSeconds: 300,
  openedAt: "2026-11-10T06:58:00Z",
};

const old: AlertListItem = {
  ...errors,
  id: "alert-old",
  block: { kind: "service", name: "billing-worker", part: null, id: "service:production/billing-worker" },
  severity: "Caution",
  worst: "Caution",
  value: 0.002,
  peak: 0.013,
  peakAt: "2026-10-16T05:40:00Z",
  openedAt: "2026-10-16T05:31:00Z",
  resolvedAt: "2026-10-16T05:58:00Z",
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

beforeEach(() => {
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.alertSummary).mockResolvedValue(summary);
  vi.mocked(api.alerts).mockResolvedValue({ items: [errors, disk], next: "page-2" });
  localStorage.clear();
});

describe("AlertsScreen", () => {
  it("counts the open alerts and the period in the chosen environment's storm", async () => {
    vi.mocked(api.home).mockResolvedValue({ ...stormSnapshot, environments: [] });
    const { container } = renderAt("/alerts");

    expect(await screen.findByRole("heading", { level: 1, name: "Alerts" })).toBeVisible();
    expect(
      await screen.findByText("2 alerts are open. 3 alerts in the last 30 days; the median cleared in 18 min."),
    ).toBeVisible();
    expect(screen.getByText("1 warning, 1 caution")).toBeVisible();
    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-storm"));
  });

  it("draws one day per cell with its worst weather and count, today last", async () => {
    renderAt("/alerts");

    const log = await screen.findByRole("region", { name: "Weather log · last 30 days" });
    const cells = within(log).getAllByRole("listitem");
    expect(cells).toHaveLength(30);
    expect(cells[29]).toHaveAccessibleName("2026-11-10: 2 alerts, worst warning");
    expect(cells[29]).toHaveTextContent("Now");
    expect(cells[4]).toHaveAccessibleName("2026-10-16: 1 alert, worst caution");
    expect(cells[0]).toHaveAccessibleName("2026-10-12: no alerts");
  });

  it("lists the alerts by day with what they say and links to their details", async () => {
    renderAt("/alerts");

    const today = await screen.findByRole("region", { name: "Today" });
    const links = within(today).getAllByRole("link");
    expect(links.map((link) => link.getAttribute("href"))).toEqual(["/alerts/alert-errors", "/alerts/alert-disk"]);
    expect(links[0]).toHaveTextContent("payments-gateway logs 21.6% errors");
    expect(links[1]).toHaveTextContent("Disk / on web-01 is full in 9 days");
    expect(links[1]).toHaveTextContent("Disk filling");
    expect(links[1]).toHaveTextContent("9 days");
  });

  it("filters by state, rule, and text through the API, and keeps the filters in the address", async () => {
    const user = userEvent.setup();
    renderAt("/alerts");
    await screen.findByRole("region", { name: "Today" });

    await user.click(screen.getByRole("button", { name: /^Open/ }));
    await user.click(screen.getByRole("button", { name: /Disk filling/ }));
    await user.type(screen.getByRole("searchbox", { name: "Service, host, or disk" }), "web");

    await waitFor(() =>
      expect(api.alerts).toHaveBeenLastCalledWith(
        { days: 30, state: "open", rules: ["disk-filling"], text: "web", environment: null },
        null,
        expect.anything(),
      ),
    );
    expect(screen.getByRole("button", { name: /^Open/ })).toHaveAttribute("aria-pressed", "true");
  });

  it("loads older alerts on request", async () => {
    const user = userEvent.setup();
    vi.mocked(api.alerts).mockResolvedValueOnce({ items: [errors, disk], next: "page-2" });
    renderAt("/alerts");
    await screen.findByRole("region", { name: "Today" });
    vi.mocked(api.alerts).mockResolvedValueOnce({ items: [old], next: null });

    await user.click(screen.getByRole("button", { name: "Show older alerts" }));

    const october = await screen.findByRole("region", { name: "Oct 16" });
    expect(within(october).getByRole("link")).toHaveTextContent("billing-worker logged 1.3% errors");
    expect(api.alerts).toHaveBeenLastCalledWith(expect.objectContaining({ days: 30 }), "page-2");
    expect(screen.queryByRole("button", { name: "Show older alerts" })).not.toBeInTheDocument();
  });

  it("lists the built-in rules with their thresholds", async () => {
    renderAt("/alerts");

    const rules = await screen.findByRole("complementary", { name: "Built-in rules" });
    expect(rules).toHaveTextContent("Full at the pace of the last day: caution within 14 days, warning within 3 days.");
    expect(rules).toHaveTextContent("p95 of a service's requests over 5 minutes: caution 1 s, warning 3 s.");
    expect(rules).toHaveTextContent("Switched off for this installation.");
  });

  it("is in the main navigation", async () => {
    renderAt("/alerts");

    expect(await screen.findByRole("link", { name: "Alerts" })).toHaveAttribute("aria-current", "page");
  });
});
