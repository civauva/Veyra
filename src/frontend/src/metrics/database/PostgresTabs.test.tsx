import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../api/client";
import { api } from "../../api/client";
import { App } from "../../app/App";
import { SessionProvider } from "../../session/SessionProvider";
import { overviewWithPostgres, postgresMetrics } from "../testMetrics";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      metricsOverview: vi.fn(),
      databaseMetrics: vi.fn(),
      metricNames: vi.fn(),
      alerts: vi.fn(),
      databaseLive: vi.fn(),
      databasePermissions: vi.fn(),
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

const path = "/metrics/databases/postgresql/pg-main";

function renderAt(where: string) {
  return render(
    <MemoryRouter initialEntries={[where]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  localStorage.clear();
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.alerts).mockResolvedValue({ items: [], next: null });
  vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithPostgres);
  vi.mocked(api.databaseMetrics).mockResolvedValue(postgresMetrics);
  vi.mocked(api.databaseLive).mockReturnValue(new Promise(() => {}));
  vi.mocked(api.databasePermissions).mockReturnValue(new Promise(() => {}));
});

describe("PostgreSQL server screen", () => {
  it("lists the server on the overview with its connections and wraparound, and leads to its screen", async () => {
    const user = userEvent.setup();
    renderAt("/metrics");

    const databases = await screen.findByRole("region", { name: "Database servers" });
    const card = within(databases).getByRole("article", { name: "pg-main" });
    expect(card).toHaveTextContent("PostgreSQL · production · 3 databases");
    expect(card).toHaveTextContent("86 of 100");
    expect(card).toHaveTextContent("Transactions, the last 24 hours");
    expect(card).toHaveTextContent("Wraparound 52%");

    await user.click(within(card).getByRole("link"));

    expect(await screen.findByRole("heading", { level: 1, name: "pg-main" })).toBeVisible();
    expect(api.databaseMetrics).toHaveBeenCalledWith("postgresql", "pg-main", "24h", false, expect.anything());
  });

  it("opens on its Overview, offers only its own tabs, and never asks SQL Server's agent", async () => {
    renderAt(path);

    const tabs = await screen.findByRole("navigation", { name: "Server views" });
    expect(
      within(tabs)
        .getAllByRole("button")
        .map((tab) => tab.textContent),
    ).toEqual(["Overview", "Databases"]);
    expect(within(tabs).getByRole("button", { name: "Overview" })).toHaveAttribute("aria-current", "page");

    const now = await screen.findByRole("region", { name: "Now" });
    expect(within(now).getByText("86 of 100")).toBeVisible();
    expect(within(now).getByText("Blocked")).toBeVisible();
    expect(within(now).getByText("631")).toBeVisible();
    expect(within(now).getByText("2.6")).toBeVisible();
    expect(within(now).getByText("6:52")).toBeVisible();
    expect(api.databaseLive).not.toHaveBeenCalled();
    expect(api.databasePermissions).not.toHaveBeenCalled();
  });

  it("charts connections against the maximum and transactions in the Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt(path);

    expect(await screen.findByRole("region", { name: "Connections" })).toBeVisible();
    expect(screen.getByRole("region", { name: "Transactions" })).toBeVisible();
  });

  it("lists each database with its size and wraparound, and the tables with the most dead rows", async () => {
    const user = userEvent.setup();
    renderAt(path);

    await user.click(await screen.findByRole("button", { name: "Databases" }));

    const databases = await screen.findByRole("region", { name: "Databases" });
    const shop = within(databases).getByRole("row", { name: /^shop / });
    expect(shop).toHaveTextContent("384 GB");
    expect(shop).toHaveTextContent("+2.9 GB");
    expect(within(shop).getByRole("meter", { name: "Transaction ID age of shop" })).toHaveAttribute(
      "aria-valuenow",
      "52",
    );
    expect(shop).toHaveTextContent("7.5%");

    const tables = screen.getByRole("region", { name: "Dead rows" });
    const carts = within(tables).getByRole("row", { name: /carts/ });
    expect(carts).toHaveTextContent("2.8M");
    expect(carts).toHaveTextContent("70%");
    expect(within(tables).getAllByRole("row")).toHaveLength(4);
  });
});
