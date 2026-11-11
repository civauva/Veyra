import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../api/client";
import { api } from "../../api/client";
import type { HistogramBucket } from "../../api/types";
import { historyItems, historySummary } from "../../alerts/testAlertHistory";
import { App } from "../../app/App";
import { SessionProvider } from "../../session/SessionProvider";
import { stormSnapshot } from "../testSnapshots";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      alertSummary: vi.fn(),
      alerts: vi.fn(),
    },
  };
});

const viewer = { id: "1", email: "viewer@example.com", displayName: "Dana Kim", role: "Viewer" as const };

/** Makes every `min-width` and `min-height` media query answer as this screen would; other queries do not match. */
function screenSize(width: number, height: number) {
  vi.stubGlobal("matchMedia", (query: string) => {
    const limits = [...query.matchAll(/min-(width|height):\s*(\d+)px/g)];

    return {
      matches:
        limits.length > 0 && limits.every(([, side, size]) => (side === "width" ? width : height) >= Number(size)),
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    };
  });
}

function renderHome() {
  return render(
    <MemoryRouter initialEntries={["/"]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

/** A full day of hours ending in the storm hour, as the API sends it since VEY-24. */
function day(): HistogramBucket[] {
  return Array.from({ length: 24 }, (_, index) => ({
    start: new Date(Date.UTC(2026, 10, 6, 8 + index)).toISOString(),
    total: 10_000,
    errors: index === 23 ? 580 : 2,
  }));
}

beforeEach(() => {
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.home).mockResolvedValue(stormSnapshot);
  vi.mocked(api.alertSummary).mockResolvedValue(historySummary);
  vi.mocked(api.alerts).mockResolvedValue({ items: historyItems, next: null });
  localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Home on large screens", () => {
  it("keeps the standard layout below 1800 px and asks for no alerts", async () => {
    screenSize(1440, 900);

    renderHome();

    expect(await screen.findByRole("region", { name: "Needs you" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Services and hosts" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Disks" })).not.toBeInTheDocument();
    expect(api.alerts).not.toHaveBeenCalled();
  });

  it("lists every service, host, and disk beside the forecast from 1800 px", async () => {
    screenSize(1920, 1080);

    renderHome();

    const blocks = await screen.findByRole("region", { name: "Services and hosts" });
    const services = within(blocks).getByRole("list", { name: "Services" });
    expect(within(services).getAllByRole("listitem")).toHaveLength(8);
    expect(within(services).getByRole("link", { name: /payments-gateway/ })).toHaveAttribute(
      "href",
      "/logs?service=payments-gateway",
    );
    expect(within(services).getByRole("link", { name: /payments-gateway/ })).toHaveTextContent("21.6%");

    // Hosts come from their alerts and from the disks they report, so a quiet host is listed too.
    const hosts = within(blocks).getByRole("list", { name: "Hosts" });
    expect(
      within(hosts)
        .getAllByRole("link")
        .map((link) => link.getAttribute("href")),
    ).toEqual(["/metrics/hosts/db-01", "/metrics/hosts/web-01"]);

    const disks = screen.getByRole("region", { name: "Disks" });
    const rows = within(disks).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("/web-0181%full in 23 days");
    expect(rows[1]).toHaveTextContent("/datadb-0158%steady");

    expect(screen.getByRole("list", { name: "Hours" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Last 24 hours" })).not.toBeInTheDocument();
    expect(api.alerts).not.toHaveBeenCalled();
  });

  it("fills a wall from 2400 by 1200 px with the day, the month, and the latest alerts", async () => {
    screenSize(3840, 2160);

    renderHome();

    const chart = await screen.findByRole("region", { name: "Last 24 hours" });
    // A day of hours observed, six expected.
    expect(within(chart).getAllByRole("listitem")).toHaveLength(30);
    expect(within(chart).getByText(/the busiest hour ahead is 10:00, 1\.6 times an average hour/)).toBeVisible();

    const month = await screen.findByRole("region", { name: "Last 30 days" });
    const days = within(month).getAllByRole("listitem");
    expect(days).toHaveLength(30);
    expect(days[29]).toHaveAccessibleName("2026-11-10: 3 alerts, worst warning");

    const latest = screen.getByRole("region", { name: "Latest alerts" });
    expect(within(latest).getByRole("link", { name: /payments-gateway logs 21\.6% errors/ })).toHaveAttribute(
      "href",
      `/alerts/${historyItems[0]?.id}`,
    );
    expect(within(latest).getByRole("link", { name: "All alerts" })).toHaveAttribute("href", "/alerts");

    expect(screen.getByRole("region", { name: "Services and hosts" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Disks" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Outlook" })).toBeInTheDocument();
    // The chart shows the hours, so the strip is not repeated.
    expect(screen.queryByRole("list", { name: "Hours" })).not.toBeInTheDocument();
    expect(api.alertSummary).toHaveBeenCalledWith(30, "production", expect.any(AbortSignal));
  });

  it("keeps three columns on a screen as wide but not as tall", async () => {
    screenSize(2560, 1080);

    renderHome();

    expect(await screen.findByRole("region", { name: "Services and hosts" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Last 24 hours" })).not.toBeInTheDocument();
  });

  it("charts the full day the API sends and marks the hour that stormed", async () => {
    screenSize(3840, 2160);
    vi.mocked(api.home).mockResolvedValue({
      ...stormSnapshot,
      hourly: { ...stormSnapshot.hourly, observed: day() },
    });

    renderHome();

    const chart = await screen.findByRole("region", { name: "Last 24 hours" });
    const bars = within(chart).getAllByRole("listitem");
    expect(bars).toHaveLength(30);
    expect(bars[23]).toHaveAccessibleName("Now: 10,000 records, 5.8% errors");
    expect(bars[23]).toHaveClass("storm");
    expect(bars[24]).toHaveAccessibleName("08:00 expected: 1.1× an average hour");
    expect(within(chart).getByText(/240,000 records in the last 24 hours/)).toBeVisible();
  });

  it("keeps six hours and now in the strip when the API sends the full day", async () => {
    screenSize(1440, 900);
    vi.mocked(api.home).mockResolvedValue({
      ...stormSnapshot,
      hourly: { ...stormSnapshot.hourly, observed: day() },
    });

    renderHome();

    const strip = await screen.findByRole("list", { name: "Hours" });
    const hours = within(strip).getAllByRole("listitem");
    expect(hours).toHaveLength(13);
    expect(hours[0]).toHaveTextContent(/^01/);
    expect(hours[6]).toHaveTextContent("Now5.8%");
  });
});
