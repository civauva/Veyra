import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { openDiskFilling, resolvedLatency } from "./testAlerts";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      alert: vi.fn(),
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
  localStorage.clear();
});

describe("AlertScreen for a disk alert", () => {
  it("says when the disk is full, how fast it grows, and how it clears", async () => {
    vi.mocked(api.alert).mockResolvedValue(openDiskFilling);

    renderAt(`/alerts/${openDiskFilling.id}`);

    expect(await screen.findByRole("heading", { level: 1, name: "Disk / on web-01 is full in 9 days" })).toBeVisible();
    expect(screen.getByText("Open · caution · 4 h 14 min")).toBeVisible();
    expect(screen.getByText(/grows 1.5 points a day and is 86% used now/)).toBeVisible();
    expect(screen.getByText(/clears once the forecast is beyond 21 days/)).toBeVisible();
  });

  it("shows the forecast, the disk's figures, the host's disks, and the way to its metrics", async () => {
    vi.mocked(api.alert).mockResolvedValue(openDiskFilling);

    renderAt(`/alerts/${openDiskFilling.id}`);

    const facts = await screen.findByText("Full in");
    expect(facts.parentElement).toHaveTextContent("9 days");
    expect(screen.getByText("In use").parentElement).toHaveTextContent("86%");
    expect(screen.getByRole("img", { name: /full on Nov 19 at the pace of the last day/ })).toBeInTheDocument();
    const disks = screen.getByRole("region", { name: "Disks of web-01" });
    expect(
      within(disks)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual(["/86% · full in 9 days", "/var/log34% · steady", "/boot22% · steady"]);
    expect(screen.getByRole("link", { name: "Open web-01's metrics" })).toHaveAttribute(
      "href",
      "/metrics/hosts/web-01",
    );
    expect(screen.queryByRole("region", { name: /Most frequent errors/ })).not.toBeInTheDocument();
  });
});

describe("AlertScreen for a latency alert", () => {
  it("says it is back, how bad it got, and which routes were slow", async () => {
    vi.mocked(api.alert).mockResolvedValue(resolvedLatency);

    renderAt(`/alerts/${resolvedLatency.id}`);

    expect(await screen.findByRole("heading", { level: 1, name: "checkout-web is back to p95 420 ms" })).toBeVisible();
    expect(screen.getByText("Resolved · was warning · lasted 25 min")).toBeVisible();
    expect(
      screen.getByText(/went above 1 s at 21:40 and above 3 s at 21:47\. It cleared at 22:05, below 800 ms/),
    ).toBeVisible();
    expect(screen.getByText("Peak").parentElement).toHaveTextContent("3.4 s");
    const routes = screen.getByRole("table");
    expect(
      within(routes)
        .getAllByRole("row")
        .slice(1)
        .map((row) => row.textContent),
    ).toEqual(["POST /cart/checkout41 /s4.1 s0.8%", "GET /cart96 /s1.6 s0.1%", "/products/{id}210 /s380 ms0%"]);
    expect(screen.getByRole("link", { name: "Open checkout-web's metrics" })).toHaveAttribute(
      "href",
      "/metrics/services/checkout-web",
    );
    expect(screen.getByRole("img", { name: /Request durations of checkout-web/ })).toBeInTheDocument();
  });
});
