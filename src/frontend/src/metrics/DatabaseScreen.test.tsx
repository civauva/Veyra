import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import type { AlertListItem } from "../api/types";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { databaseMetrics, overviewWithDatabase } from "./testMetrics";

vi.mock("../api/client", async (importOriginal) => {
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
      // The tabs' own reads; each test that needs one gives it an answer.
      databaseLive: vi.fn(),
      databaseStorage: vi.fn(),
      databaseIndexes: vi.fn(),
    },
  };
});

const viewer = {
  id: "1",
  email: "viewer@example.com",
  displayName: "Dana Kim",
  role: "Viewer" as const,
  timeZone: null,
};

const path = "/metrics/databases/microsoft.sql_server/sql-prod-01";

/** The open backup alert on shop, as the alerts API lists it. */
const backupAlert: AlertListItem = {
  id: "0193a2b4-0000-7000-8000-000000000077",
  rule: "db-backup-age",
  environment: "production",
  block: {
    kind: "database",
    name: "microsoft.sql_server sql-prod-01",
    part: "shop",
    id: "database:production/microsoft.sql_server sql-prod-01",
  },
  severity: "Caution",
  worst: "Caution",
  value: 31 * 3600,
  peak: 31 * 3600,
  peakAt: null,
  threshold: 26 * 3600,
  windowSeconds: 300,
  openedAt: "2026-11-10T03:00:00Z",
  updatedAt: "2026-11-10T11:00:00Z",
  resolvedAt: null,
};

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
  vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithDatabase);
  vi.mocked(api.databaseMetrics).mockResolvedValue(databaseMetrics);
  vi.mocked(api.databaseLive).mockReturnValue(new Promise(() => {}));
  vi.mocked(api.databaseStorage).mockReturnValue(new Promise(() => {}));
  vi.mocked(api.databaseIndexes).mockReturnValue(new Promise(() => {}));
});

describe("DatabaseScreen", () => {
  it("lists the SQL Server on the overview and leads to its screen", async () => {
    const user = userEvent.setup();
    renderAt("/metrics");

    const databases = await screen.findByRole("region", { name: "Databases" });
    const card = within(databases).getByRole("article", { name: "sql-prod-01" });
    expect(card).toHaveTextContent("SQL Server · production · 5 databases");
    expect(card).toHaveTextContent("1 job failed");

    await user.click(within(card).getByRole("link"));

    expect(await screen.findByRole("heading", { level: 1, name: "sql-prod-01" })).toBeVisible();
    expect(api.databaseMetrics).toHaveBeenCalledWith("microsoft.sql_server", "sql-prod-01", "6h", expect.anything());
  });

  it("opens on the Live tab and offers the Simple view's tabs", async () => {
    renderAt(path);

    const tabs = await screen.findByRole("navigation", { name: "Server views" });
    expect(within(tabs).getByRole("button", { name: "Live" })).toHaveAttribute("aria-current", "page");
    expect(within(tabs).queryByRole("button", { name: "Waits" })).not.toBeInTheDocument();
    expect(api.databaseLive).toHaveBeenCalled();
  });

  it("lists the databases with their last backup, and keeps growth to the Advanced view", async () => {
    renderAt(`${path}?tab=databases`);

    const databases = await screen.findByRole("region", { name: "Databases" });
    expect(within(databases).getByRole("row", { name: /^shop / })).toHaveTextContent("1 d 7 h ago");
    expect(within(databases).getByRole("row", { name: /tempdb/ })).toHaveTextContent("not backed up");
    expect(within(databases).queryByText("Growth / day")).not.toBeInTheDocument();
  });

  it("adds the Waits tab with page life, waits, and sessions in the Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt(`${path}?tab=waits`);

    const now = await screen.findByRole("region", { name: "Now" });
    expect(within(now).getByText("1,240")).toBeVisible();
    expect(within(now).getByText("Page life")).toBeVisible();
    expect(screen.getByRole("region", { name: "Waits" })).toBeVisible();
    expect(screen.getByRole("region", { name: "Sessions" })).toBeVisible();
  });

  it("shows growth per day on the Databases tab in the Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt(`${path}?tab=databases`);

    const databases = await screen.findByRole("region", { name: "Databases" });
    expect(within(databases).getByText("Growth / day")).toBeVisible();
  });

  it("names the worst open alert of the server first", async () => {
    vi.mocked(api.alerts).mockResolvedValue({ items: [backupAlert], next: null });
    renderAt(path);

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("SQL Server sql-prod-01: shop has no full backup for 1 d 7 h.");
    expect(within(status).getByRole("link", { name: "Alert details" })).toHaveAttribute(
      "href",
      `/alerts/${backupAlert.id}`,
    );
  });

  it("says when the server sent nothing within the range", async () => {
    vi.mocked(api.databaseMetrics).mockRejectedValue(new ApiError(404, "Not found"));
    renderAt(`${path}?tab=databases`);

    expect(await screen.findByText("sql-prod-01 sent no metrics within the last 6h.")).toBeVisible();
  });
});
