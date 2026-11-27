import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { DatabaseServerRow, HostRow, ResourcePage, ServiceRow, ServicesHeader } from "../api/types";
import { DatabasesScreen } from "./DatabasesScreen";
import { HostsScreen } from "./HostsScreen";
import { ServicesScreen } from "./ServicesScreen";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: { resourceServices: vi.fn(), resourceHosts: vi.fn(), resourceDatabases: vi.fn() },
  };
});

function service(name: string, health: ServiceRow["health"], silent = false): ServiceRow {
  return {
    name,
    environment: "production",
    health,
    silent,
    requestsPerMinute: 680,
    p95Seconds: 0.108,
    errorRatio: 0.084,
    instances: 3,
    hosts: ["docker-02"],
    lastSeenAt: "2026-11-26T08:59:00Z",
    requests: [1, 3, 2, 5],
  };
}

const servicesHeader: ServicesHeader = {
  total: 12,
  healthy: 10,
  warning: 1,
  caution: 1,
  silent: 1,
  requests: 1_240_000,
  requestsChange: 0.12,
  errorRatio: 0.0038,
  errorRatioChange: 0.0012,
  p95Seconds: 0.214,
  p95Change: 0,
  instances: 28,
  hosts: 9,
};

function servicesPage(items: ServiceRow[], next: string | null = null): ResourcePage<ServicesHeader, ServiceRow> {
  return { header: servicesHeader, items, total: 12, next };
}

function renderAt(path: string, element: ReactNode) {
  return render(<MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>);
}

beforeEach(() => {
  vi.mocked(api.resourceServices).mockResolvedValue(servicesPage([service("CustomerSync", "Warning")], "page-2"));
});

describe("Services", () => {
  it("ServicesScreen_Header_ShowsTheServersFiguresAndChanges", async () => {
    renderAt("/resources/services", <ServicesScreen />);

    const figures = await screen.findByRole("region", { name: "Figures" });
    expect(await within(figures).findByText("1.24M")).toBeVisible();
    expect(within(figures).getByText("↑ 12% vs the period before")).toBeVisible();
    expect(within(figures).getByText("↑ 0.12 pp vs the period before")).toBeVisible();
    expect(screen.getByText("1 warning · 1 caution")).toBeVisible();
  });

  it("ServicesScreen_Rows_KeepTheServersOrderAndLeadToTheService", async () => {
    vi.mocked(api.resourceServices).mockResolvedValue(
      servicesPage([service("CustomerSync", "Warning"), service("Import.Worker", "Ok", true)]),
    );
    renderAt("/resources/services", <ServicesScreen />);

    expect(await screen.findByText("Import.Worker")).toBeVisible();
    const rows = screen.getAllByRole("row");
    expect(rows[1]).toHaveTextContent("CustomerSync");
    expect(rows[2]).toHaveTextContent("Silent");
    expect(screen.getByRole("link", { name: "CustomerSync" })).toHaveAttribute(
      "href",
      "/metrics/services/CustomerSync",
    );
  });

  it("ServicesScreen_Filter_AsksTheServerWithIt", async () => {
    renderAt("/resources/services", <ServicesScreen />);
    await screen.findByText("CustomerSync");

    await userEvent.selectOptions(screen.getByRole("combobox", { name: /Health/ }), "warning");

    await waitFor(() =>
      expect(vi.mocked(api.resourceServices).mock.calls.at(-1)?.[0].filters).toEqual({ health: "warning" }),
    );
  });

  it("ServicesScreen_LoadMore_AddsTheNextPage", async () => {
    vi.mocked(api.resourceServices).mockImplementation((query) =>
      Promise.resolve(
        query.cursor === "page-2"
          ? servicesPage([service("Orders.API", "Ok")])
          : servicesPage([service("CustomerSync", "Warning")], "page-2"),
      ),
    );
    renderAt("/resources/services", <ServicesScreen />);

    await userEvent.click(await screen.findByRole("button", { name: "Load more" }));

    expect(await screen.findByText("Orders.API")).toBeVisible();
    expect(screen.getByText("CustomerSync")).toBeVisible();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument());
  });
});

describe("Hosts and databases", () => {
  it("HostsScreen_SilentHost_StaysListedWithTheDiskThatFillsFirst", async () => {
    const host = (name: string, silent: boolean): HostRow => ({
      name,
      kind: "host",
      os: "Linux",
      parent: null,
      health: "Ok",
      silent,
      cpuRatio: silent ? null : 0.18,
      memoryRatio: 0.43,
      fullestDiskRatio: 0.89,
      runs: ["Orders.API"],
      runsCount: 1,
      lastSeenAt: "2026-11-26T08:54:00Z",
    });
    vi.mocked(api.resourceHosts).mockResolvedValue({
      header: {
        known: 12,
        online: 11,
        containers: 6,
        cpuAverage: 0.31,
        cpuBusiest: { name: "payments-5c2", ratio: 0.71 },
        memoryAverage: 0.58,
        memoryFullest: null,
        disksFilling: 1,
        disksFillingFirst: { host: "sql-01", mount: "D:", hoursLeft: 38 },
      },
      items: [host("branch-01", true), host("web-01", false)],
      total: 2,
      next: null,
    });
    renderAt("/resources/hosts", <HostsScreen />);

    expect(await screen.findByText("branch-01")).toBeVisible();
    expect(screen.getByText("Silent", { selector: ".rs-pill" })).toBeVisible();
    expect(screen.getByText("sql-01 D: full in ~38 h")).toBeVisible();
    expect(screen.getByText("1 silent · 1 disk filling")).toBeVisible();
  });

  it("DatabasesScreen_LateBackup_IsNamedAndRed", async () => {
    const server: DatabaseServerRow = {
      engine: "microsoft.sql_server",
      server: "SQL-PROD-01",
      version: "SQL Server 2022 CU14",
      health: "Caution",
      silent: false,
      databases: 13,
      sizeBytes: 382_000_000_000,
      growthBytesPerDay: 1_800_000_000,
      lastFullBackupAgeSeconds: 21_600,
      lateBackups: 1,
      blockingChains: 3,
      cpuRatio: 0.28,
    };
    vi.mocked(api.resourceDatabases).mockResolvedValue({
      header: {
        servers: 6,
        silent: 1,
        databases: 35,
        dataBytes: 2_100_000_000_000,
        growthBytes: 14_000_000_000,
        lateBackups: 1,
        lateBackupFirst: { server: "SQL-PROD-01", database: "Reporting" },
        blockingChains: 3,
        blockingServer: "SQL-PROD-01",
      },
      items: [server],
      total: 1,
      next: null,
    });
    renderAt("/resources/databases", <DatabasesScreen />);

    expect(await screen.findByText("Reporting on SQL-PROD-01")).toBeVisible();
    expect(screen.getByRole("link", { name: "SQL-PROD-01" })).toHaveAttribute(
      "href",
      "/metrics/databases/microsoft.sql_server/SQL-PROD-01",
    );
    expect(screen.getByText(/1 late/)).toHaveClass("rs-bad");
  });
});
