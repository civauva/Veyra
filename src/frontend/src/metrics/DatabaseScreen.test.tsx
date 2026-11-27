import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import type { AlertListItem, DatabaseMetrics, StorageFinding } from "../api/types";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import {
  asServer,
  availabilityView,
  blockingView,
  configurationView,
  eventsView,
  indexesView,
  jobsSimpleView,
  jobsView,
  liveQuietView,
  liveView,
  monthlyReportHash,
  permissionsAzureDatabase,
  permissionsGranted,
  permissionsManagedInstance,
  monthlyReportRow,
  queriesSimpleView,
  queriesView,
  queryView,
  serverView,
  sqlProdHeadBlockerAlert,
  storageView,
} from "./database/testDatabase";
import { databaseMetrics, overviewWithDatabase, overviewWithPostgres, postgresMetrics } from "./testMetrics";

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
      alertAreas: vi.fn(),
      // The tabs' own reads; each test that needs one gives it an answer.
      databaseLive: vi.fn(),
      databaseStorage: vi.fn(),
      databaseIndexes: vi.fn(),
      databasePermissions: vi.fn(),
      databaseQueries: vi.fn(),
      databaseQuery: vi.fn(),
      databaseBlocking: vi.fn(),
      databaseJobs: vi.fn(),
      databaseEvents: vi.fn(),
      databaseServer: vi.fn(),
      databaseAvailability: vi.fn(),
      databaseConfiguration: vi.fn(),
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
  area: "databases",
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
  // Without alerts the server answers every area fine, in the order of the tabs.
  vi.mocked(api.alertAreas).mockImplementation((_block, areas) =>
    Promise.resolve(areas.map((area) => ({ area, health: "Ok" as const, alerts: [] }))),
  );
  vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithDatabase);
  vi.mocked(api.databaseMetrics).mockImplementation((_engine, _server, _range, system) =>
    Promise.resolve(metricsFor(system)),
  );
  vi.mocked(api.databaseLive).mockReturnValue(new Promise(() => {}));
  vi.mocked(api.databaseStorage).mockReturnValue(new Promise(() => {}));
  vi.mocked(api.databaseIndexes).mockReturnValue(new Promise(() => {}));
  vi.mocked(api.databasePermissions).mockResolvedValue(permissionsGranted);
  // The server keeps the Simple view to what it shows (VEY-67), so each mock answers by it.
  vi.mocked(api.databaseQueries).mockImplementation((_engine, _server, _range, _system, _sort, simple) =>
    Promise.resolve(simple ? queriesSimpleView : queriesView),
  );
  vi.mocked(api.databaseQuery).mockResolvedValue(queryView);
  vi.mocked(api.databaseBlocking).mockResolvedValue(blockingView);
  vi.mocked(api.databaseJobs).mockImplementation((_engine, _server, _system, simple) =>
    Promise.resolve(simple ? jobsSimpleView : jobsView),
  );
  vi.mocked(api.databaseEvents).mockResolvedValue(eventsView);
  vi.mocked(api.databaseServer).mockResolvedValue(serverView);
  vi.mocked(api.databaseAvailability).mockResolvedValue(availabilityView);
  vi.mocked(api.databaseConfiguration).mockResolvedValue(configurationView);
});

/** The SQL Server's screen as the server answers it: its own databases left out unless System is on (VEY-66). */
function metricsFor(system: boolean): DatabaseMetrics {
  const databases = asServer(databaseMetrics.databases, system, (database) => database.name);

  return { ...databaseMetrics, databases: databases.rows, hiddenDatabases: databases.hidden };
}

/**
 * Checks that each tile or panel named has its question mark (VEY-60), and that the first opens its explanation.
 *
 * @param names - The tiles' and panels' names, each found as the button "What {name} means".
 * @param explains - What the first one's explanation says.
 */
async function expectHelp(names: string[], explains: RegExp) {
  const user = userEvent.setup();
  const [first, ...rest] = names;

  const button = (await screen.findAllByRole("button", { name: `What ${first} means` }))[0];
  for (const name of rest) {
    expect(screen.getAllByRole("button", { name: `What ${name} means` }).length).toBeGreaterThan(0);
  }

  await user.click(button as HTMLElement);

  expect(screen.getByRole("tooltip")).toHaveTextContent(explains);
}

describe("DatabaseScreen", () => {
  it("lists the SQL Server on the overview and leads to its screen", async () => {
    const user = userEvent.setup();
    renderAt("/metrics");

    const databases = await screen.findByRole("region", { name: "Database servers" });
    const card = within(databases).getByRole("article", { name: "sql-prod-01" });
    expect(card).toHaveTextContent("SQL Server · production · 5 databases");
    expect(card).toHaveTextContent("1 job failed");

    await user.click(within(card).getByRole("link"));

    expect(await screen.findByRole("heading", { level: 1, name: "sql-prod-01" })).toBeVisible();
    expect(api.databaseMetrics).toHaveBeenCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      "24h",
      false,
      expect.anything(),
    );
  });

  it("opens on the Overview tab and offers the Simple view's tabs", async () => {
    renderAt(path);

    const tabs = await screen.findByRole("navigation", { name: "Server views" });
    expect(within(tabs).getByRole("button", { name: "Overview" })).toHaveAttribute("aria-current", "page");
    expect(within(tabs).getByRole("button", { name: "Live" })).toBeVisible();
    expect(within(tabs).queryByRole("button", { name: "Waits" })).not.toBeInTheDocument();
    expect(api.databaseLive).not.toHaveBeenCalled();
  });

  it("puts the trail and the view toggle in the bar, and the range only on tabs that use one", async () => {
    const user = userEvent.setup();
    renderAt(`${path}?tab=queries`);

    const trail = await screen.findByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Metrics" })).toHaveAttribute("href", "/metrics?range=24h");
    expect(within(trail).getByRole("link", { name: "Database servers" })).toHaveAttribute(
      "href",
      "/metrics?range=24h#metrics-databases",
    );
    expect(within(trail).getByRole("button", { name: /^sql-prod-01/ })).toBeVisible();
    expect(screen.queryByRole("link", { name: "← Metrics" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("group", { name: "View" })).toHaveLength(1);
    expect(screen.getByRole("button", { name: "24h" })).toBeVisible();

    const tabs = screen.getByRole("navigation", { name: "Server views" });
    await user.click(within(tabs).getByRole("button", { name: "Live" }));

    expect(screen.queryByRole("button", { name: "24h" })).not.toBeInTheDocument();
  });

  it("lists the databases with their last backup, and keeps growth to the Advanced view", async () => {
    const user = userEvent.setup();
    renderAt(`${path}?tab=databases`);

    const databases = await screen.findByRole("region", { name: "Databases" });
    expect(within(databases).getByRole("row", { name: /^shop / })).toHaveTextContent("1 d 7 h ago");

    // tempdb is the server's own, so it shows only once System is on (VEY-64).
    expect(within(databases).queryByRole("row", { name: /tempdb/ })).not.toBeInTheDocument();
    expect(within(databases).getByRole("note")).toHaveTextContent("1 system database hidden.");
    await user.click(within(databases).getByRole("button", { name: "Show them" }));
    expect(await within(databases).findByRole("row", { name: /tempdb/ })).toHaveTextContent("not backed up");
    expect(api.databaseMetrics).toHaveBeenLastCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      "24h",
      true,
      expect.anything(),
    );
    expect(screen.getByRole("button", { name: "System" })).toHaveAttribute("aria-pressed", "true");
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

    expect(await screen.findByText("sql-prod-01 sent no metrics within the last 24 hours.")).toBeVisible();
  });

  it("leaves out the tabs an Azure SQL Database does not have, and says why", async () => {
    localStorage.setItem("veyra.view", "advanced");
    vi.mocked(api.databasePermissions).mockResolvedValue(permissionsAzureDatabase);
    renderAt(`${path}?tab=jobs`);

    expect(await screen.findByText(/An Azure SQL Database is one database/)).toBeVisible();
    const tabs = screen.getByRole("navigation", { name: "Server views" });
    expect(within(tabs).getByRole("button", { name: "Overview" })).toHaveAttribute("aria-current", "page");
    for (const name of ["Waits", "Jobs", "Events", "Server"]) {
      expect(within(tabs).queryByRole("button", { name })).not.toBeInTheDocument();
    }
    expect(within(tabs).getByRole("button", { name: "Databases" })).toBeVisible();
  });

  it("says on a Managed Instance's Databases tab that Azure takes the backups", async () => {
    vi.mocked(api.databasePermissions).mockResolvedValue(permissionsManagedInstance);
    renderAt(`${path}?tab=databases`);

    expect(await screen.findByText(/Azure takes the backups and runs the storage/)).toBeVisible();
    expect(
      within(screen.getByRole("navigation", { name: "Server views" })).getByRole("button", { name: "Jobs" }),
    ).toBeVisible();
  });
});

describe("DatabaseScreen help", () => {
  it("explains each tile of the Live tab's Simple view, and a tile that opens still opens", async () => {
    const user = userEvent.setup();
    vi.mocked(api.databaseLive).mockResolvedValue(liveView);
    renderAt(`${path}?tab=live`);

    await expectHelp(
      [
        "Oldest open transaction",
        "Requests",
        "Waiting on others",
        "Longest request",
        "Who waits for whom",
        "Running now",
      ],
      /transaction/,
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Oldest open transaction: 6:52. Open its connection" }));

    expect(screen.getByRole("dialog")).toBeVisible();
  });

  it("explains each tile of the Live tab's Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    vi.mocked(api.databaseLive).mockResolvedValue(liveView);
    renderAt(`${path}?tab=live`);

    await expectHelp(
      [
        "tempdb free",
        "Findings",
        "Batch requests",
        "Blocked",
        "Longest request",
        "Oldest open transaction",
        "Grants waiting",
        "Blocking now",
        "Sessions",
      ],
      /tempdb/,
    );
  });

  it("explains the figures and charts of the Waits tab", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt(`${path}?tab=waits`);

    await expectHelp(
      ["Page life", "Batch requests", "Connections", "Blocked", "Deadlocks", "Longest query", "Waits", "Sessions"],
      /memory/,
    );
    expect(await screen.findByRole("button", { name: "What Wait types means" })).toBeVisible();
  });

  it("hides the server's own statements until System is switched on in the bar", async () => {
    const user = userEvent.setup();
    localStorage.setItem("veyra.view", "advanced");
    const catalogRead = {
      ...monthlyReportRow,
      findings: [],
      system: true,
      queryHash: "0x5ys",
      database: "master",
      name: "Read from sys.tables",
      text: "SELECT SCHEMA_NAME(tbl.schema_id) AS [Schema] FROM sys.tables AS tbl",
      totalSeconds: 900,
    };
    // The server leaves the catalog read out unless System is on (VEY-66).
    vi.mocked(api.databaseQueries).mockImplementation((_engine, _server, _range, system) => {
      const statements = asServer([catalogRead, ...queriesView.statements], system, (row) => row.database);

      return Promise.resolve({ ...queriesView, statements: statements.rows, hiddenStatements: statements.hidden });
    });
    renderAt(`${path}?tab=queries`);

    const ranking = await screen.findByRole("region", { name: "Statements" });
    expect(within(ranking).queryByRole("link", { name: "Read from sys.tables" })).not.toBeInTheDocument();
    expect(within(ranking).getAllByRole("row")).toHaveLength(3);
    expect(within(ranking).getByRole("note")).toHaveTextContent("1 system statement hidden.");

    const toggle = screen.getByRole("button", { name: "System" });
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    await user.click(toggle);

    const row = await within(ranking).findByRole("row", { name: /Read from sys.tables/ });
    expect(row).toHaveTextContent("system");
    expect(within(ranking).queryByRole("note")).not.toBeInTheDocument();
  });

  it("explains the panels of the Queries and Blocking tabs", async () => {
    renderAt(`${path}?tab=queries`);

    await expectHelp(["Query Store is off", "Queries worth a look"], /Query Store/);

    await userEvent.setup().click(screen.getByRole("button", { name: "Blocking" }));

    expect(await screen.findByRole("button", { name: "What Deadlocks means" })).toBeVisible();
    expect(screen.getByRole("button", { name: "What Blocking means" })).toBeVisible();
  });

  it("explains every panel of the Databases tab in the Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    vi.mocked(api.databaseStorage).mockResolvedValue(storageView);
    vi.mocked(api.databaseIndexes).mockResolvedValue(indexesView);
    renderAt(`${path}?tab=databases`);

    await expectHelp(["Volumes", "Databases", "Needs doing", "Each database", "Indexes and statistics"], /volume/i);
  });

  it("lists the findings the server merged and ranked, in its order, each leading to its database", async () => {
    const [first] = storageView.needsDoing as [StorageFinding];
    const volume = {
      finding: { ...first.finding, kind: "volume", severity: "caution" as const, title: "D: fills in 41 days" },
      database: null,
    };
    // A caution before a warning proves the screen keeps the server's order (VEY-68).
    vi.mocked(api.databaseStorage).mockResolvedValue({ ...storageView, needsDoing: [volume, first] });
    renderAt(`${path}?tab=databases`);

    const panel = await screen.findByRole("region", { name: "Needs doing" });
    const items = within(panel).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("D: fills in 41 days");
    expect(items[1]).toHaveTextContent("shop has no full backup");
    expect(within(items[1] as HTMLElement).getByRole("button", { name: /shop/ })).toBeVisible();
  });

  it("asks the server for the five worst index findings in the Simple view", async () => {
    renderAt(`${path}?tab=databases`);

    await waitFor(() =>
      expect(api.databaseIndexes).toHaveBeenLastCalledWith(
        "microsoft.sql_server",
        "sql-prod-01",
        false,
        true,
        expect.anything(),
      ),
    );
  });

  it("asks the server for the Simple view of the jobs and the events, and lists its answer", async () => {
    const user = userEvent.setup();
    renderAt(`${path}?tab=jobs`);

    expect(await screen.findByText("Nightly full backup - shop")).toBeVisible();
    expect(screen.queryByText("Index maintenance")).not.toBeInTheDocument();
    expect(api.databaseJobs).toHaveBeenLastCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      false,
      true,
      expect.anything(),
    );

    await user.click(screen.getByRole("button", { name: "Advanced" }));

    expect(await screen.findByText("Index maintenance")).toBeVisible();
    expect(api.databaseJobs).toHaveBeenLastCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      false,
      false,
      expect.anything(),
    );

    await user.click(
      within(screen.getByRole("navigation", { name: "Server views" })).getByRole("button", { name: "Events" }),
    );
    await waitFor(() =>
      expect(api.databaseEvents).toHaveBeenLastCalledWith(
        "microsoft.sql_server",
        "sql-prod-01",
        "24h",
        false,
        expect.anything(),
      ),
    );
  });

  it("explains the Jobs, Events, and Server tabs", async () => {
    const user = userEvent.setup();
    localStorage.setItem("veyra.view", "advanced");
    renderAt(`${path}?tab=jobs`);

    await expectHelp(["SQL Agent jobs"], /msdb/);

    const tabs = screen.getByRole("navigation", { name: "Server views" });
    await user.click(within(tabs).getByRole("button", { name: "Events" }));
    expect(await screen.findByRole("button", { name: "What Events means" })).toBeVisible();

    await user.click(within(tabs).getByRole("button", { name: "Server" }));
    expect(await screen.findByRole("button", { name: "What The server means" })).toBeVisible();
    for (const name of ["Availability", "Configuration and security", "CPU, memory, and workload"]) {
      expect(await screen.findByRole("button", { name: `What ${name} means` })).toBeVisible();
    }
  });

  it("explains each figure and panel of a statement's page", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt(`${path}/queries/${monthlyReportHash}`);

    await expectHelp(
      [
        "Reads per run",
        "Runs",
        "Total time",
        "Time per run",
        "CPU per run",
        "Plans",
        "How to fix it",
        "Who runs it",
        "The statement",
      ],
      /logical reads/,
    );
  });

  it("explains the rings, figures, and tables of a PostgreSQL server", async () => {
    const user = userEvent.setup();
    localStorage.setItem("veyra.view", "advanced");
    vi.mocked(api.databaseMetrics).mockResolvedValue(postgresMetrics);
    renderAt("/metrics/databases/postgresql/pg-main");

    await expectHelp(
      [
        "Wraparound",
        "Now",
        "Connections",
        "Blocked",
        "Deadlocks",
        "Longest query",
        "Commits",
        "Rollbacks",
        "Transactions",
      ],
      /2\.1 billion/,
    );

    await user.click(
      within(screen.getByRole("navigation", { name: "Server views" })).getByRole("button", {
        name: "Databases",
      }),
    );

    expect(await screen.findByRole("button", { name: "What Databases means" })).toBeVisible();
    expect(screen.getByRole("button", { name: "What Dead rows means" })).toBeVisible();
  });
});

describe("DatabaseScreen overview", () => {
  it("shows each area as weather in the order the server ranks them", async () => {
    vi.mocked(api.alerts).mockResolvedValue({ items: [sqlProdHeadBlockerAlert, backupAlert], next: null });
    // The server ranks the areas from the open alerts, the worst first (VEY-68); the screen keeps its order.
    vi.mocked(api.alertAreas).mockImplementation((_block, areas) =>
      Promise.resolve([
        { area: "blocking", health: "Warning" as const, alerts: [sqlProdHeadBlockerAlert] },
        { area: "databases", health: "Caution" as const, alerts: [backupAlert] },
        ...areas
          .filter((area) => area !== "blocking" && area !== "databases")
          .map((area) => ({ area, health: "Ok" as const, alerts: [] })),
      ]),
    );
    renderAt(path);

    const areas = await screen.findByRole("list", { name: "Areas" });
    const cards = within(areas).getAllByRole("listitem");
    expect(cards.map((card) => within(card).getByRole("heading", { level: 2 }).textContent)).toEqual([
      "Blocking",
      "Databases",
      "Live",
      "Queries",
      "Jobs",
      "Events",
      "Server",
    ]);
    const [blocking, databases, , , jobs] = cards as HTMLElement[];
    expect(within(blocking as HTMLElement).getByRole("button", { name: /^Blocking, storm: / })).toBeVisible();
    expect(blocking).toHaveTextContent("1 alert");
    expect(
      within(databases as HTMLElement).getByRole("button", { name: /^Databases, showers: 1 needs doing/ }),
    ).toBeVisible();
    expect(databases).toHaveTextContent("shop has no full backup for 1 d 7 h");
    expect(within(jobs as HTMLElement).getByRole("button", { name: /^Jobs, fine: 1 failed/ })).toBeVisible();
    expect(jobs).toHaveTextContent("Nightly full backup - shop");
    expect(screen.getByRole("button", { name: "What the Blocking card means" })).toBeVisible();
    expect(api.alertAreas).toHaveBeenCalledWith(
      "microsoft.sql_server sql-prod-01",
      ["live", "queries", "blocking", "databases", "jobs", "events", "server"],
      expect.anything(),
    );
  });

  it("adds the Waits area in the Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt(path);

    const areas = await screen.findByRole("list", { name: "Areas" });
    expect(within(areas).getByRole("button", { name: /^Waits, fine: mostly CPU/ })).toBeVisible();
  });

  it("opens an area's tab from its card", async () => {
    const user = userEvent.setup();
    renderAt(path);

    await user.click(await screen.findByRole("button", { name: /^Databases, / }));

    const tabs = screen.getByRole("navigation", { name: "Server views" });
    expect(within(tabs).getByRole("button", { name: "Databases" })).toHaveAttribute("aria-current", "page");
    expect(await screen.findByRole("region", { name: "Databases" })).toBeVisible();
  });
});

describe("DatabaseScreen server switcher", () => {
  beforeEach(() => {
    vi.mocked(api.metricsOverview).mockResolvedValue(overviewWithPostgres);
    vi.mocked(api.databaseMetrics).mockImplementation((engine) =>
      Promise.resolve(engine === "postgresql" ? postgresMetrics : databaseMetrics),
    );
  });

  it("lists every database server with its engine and environment", async () => {
    const user = userEvent.setup();
    renderAt(`${path}?tab=databases`);

    const button = await screen.findByRole("button", { name: /^sql-prod-01/ });
    expect(button).toHaveAttribute("aria-haspopup", "menu");
    await user.click(button);

    const menu = await screen.findByRole("menu", { name: "Database servers" });
    const items = await within(menu).findAllByRole("menuitem");
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent("pg-main");
    expect(items[0]).toHaveTextContent("PostgreSQL · production");
    expect(items[1]).toHaveTextContent("SQL Server · production");
    expect(items[1]).toHaveAttribute("aria-current", "true");
  });

  it("lists the servers in the order the server sends them", async () => {
    const user = userEvent.setup();
    const reversed = [...overviewWithPostgres.databases].reverse();
    vi.mocked(api.metricsOverview).mockResolvedValue({ ...overviewWithPostgres, databases: reversed });
    renderAt(`${path}?tab=databases`);

    await user.click(await screen.findByRole("button", { name: /^sql-prod-01/ }));

    const items = await within(await screen.findByRole("menu", { name: "Database servers" })).findAllByRole("menuitem");
    expect(items.map((item) => item.textContent)).toEqual(reversed.map((card) => expect.stringContaining(card.server)));
  });

  it("lists the server shown even when the overview of the range has none", async () => {
    const user = userEvent.setup();
    vi.mocked(api.metricsOverview).mockResolvedValue({ ...overviewWithPostgres, databases: [] });
    renderAt(`${path}?tab=databases`);

    await screen.findByRole("heading", { level: 1, name: "sql-prod-01" });
    await user.click(await screen.findByRole("button", { name: /^sql-prod-01/ }));

    const menu = await screen.findByRole("menu", { name: "Database servers" });
    const item = await within(menu).findByRole("menuitem");
    expect(item).toHaveTextContent("sql-prod-01");
    expect(item).toHaveAttribute("aria-current", "true");
  });

  it("moves to another server by keyboard and keeps the tab it has", async () => {
    const user = userEvent.setup();
    renderAt(`${path}?tab=databases`);

    const button = await screen.findByRole("button", { name: /^sql-prod-01/ });
    button.focus();
    await user.keyboard("{Enter}");

    const menu = await screen.findByRole("menu", { name: "Database servers" });
    await within(menu).findAllByRole("menuitem");
    await user.keyboard("{ArrowDown}");
    expect(within(menu).getByRole("menuitem", { name: /pg-main/ })).toHaveFocus();
    await user.keyboard("{Enter}");

    expect(await screen.findByRole("heading", { level: 1, name: "pg-main" })).toBeVisible();
    const tabs = screen.getByRole("navigation", { name: "Server views" });
    expect(within(tabs).getByRole("button", { name: "Databases" })).toHaveAttribute("aria-current", "page");
  });

  it("closes on Escape and gives the focus back", async () => {
    const user = userEvent.setup();
    renderAt(path);

    const button = await screen.findByRole("button", { name: /^sql-prod-01/ });
    await user.click(button);
    expect(await screen.findByRole("menu", { name: "Database servers" })).toBeVisible();

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });
});

describe("DatabaseScreen drill-down", () => {
  it("leads from the Blocked tile to the blocking tree", async () => {
    const user = userEvent.setup();
    localStorage.setItem("veyra.view", "advanced");
    vi.mocked(api.databaseLive).mockResolvedValue(liveView);
    renderAt(`${path}?tab=live`);

    await user.click(await screen.findByRole("button", { name: "Blocked: 1. See who blocks whom" }));

    expect(document.getElementById("live-tree")).toHaveFocus();
  });

  it("leads from Waiting on others to who waits for whom in the Simple view", async () => {
    const user = userEvent.setup();
    vi.mocked(api.databaseLive).mockResolvedValue(liveView);
    renderAt(`${path}?tab=live`);

    await user.click(await screen.findByRole("button", { name: "Waiting on others: 1. See who waits for whom" }));

    expect(screen.getByRole("region", { name: "Who waits for whom" })).toHaveFocus();
  });

  it("opens the longest request's connection from its tile", async () => {
    const user = userEvent.setup();
    vi.mocked(api.databaseLive).mockResolvedValue(liveView);
    renderAt(`${path}?tab=live`);

    await user.click(await screen.findByRole("button", { name: "Longest request: 2:14. Open the request" }));

    expect(screen.getByRole("dialog")).toHaveTextContent("117");
  });

  it("leaves a tile with nothing to show plain", async () => {
    vi.mocked(api.databaseLive).mockResolvedValue(liveQuietView);
    renderAt(`${path}?tab=live`);

    expect(await screen.findByText("nobody waits")).toBeVisible();
    expect(screen.queryByRole("button", { name: /^Waiting on others:/ })).not.toBeInTheDocument();
  });

  it("leads from the Waits tab's figures to the Blocking and Live tabs", async () => {
    const user = userEvent.setup();
    localStorage.setItem("veyra.view", "advanced");
    renderAt(`${path}?tab=waits`);

    const now = await screen.findByRole("region", { name: "Now" });
    // No session is blocked in the fixture, so its tile stays plain.
    expect(within(now).queryByRole("button", { name: /^Blocked:/ })).not.toBeInTheDocument();
    await user.click(within(now).getByRole("button", { name: "Deadlocks: 2. See them on Blocking" }));

    const tabs = screen.getByRole("navigation", { name: "Server views" });
    expect(within(tabs).getByRole("button", { name: "Blocking" })).toHaveAttribute("aria-current", "page");

    await user.click(within(tabs).getByRole("button", { name: "Waits" }));
    await user.click(await screen.findByRole("button", { name: "Longest query: 2:14. See it live" }));

    expect(within(tabs).getByRole("button", { name: "Live" })).toHaveAttribute("aria-current", "page");
  });

  it("leads from a database's entry in Needs doing to its row", async () => {
    const user = userEvent.setup();
    vi.mocked(api.databaseStorage).mockResolvedValue(storageView);
    renderAt(`${path}?tab=databases`);

    const needs = await screen.findByRole("region", { name: "Needs doing" });
    await user.click(within(needs).getByRole("button", { name: "See shop in the list" }));

    const databases = screen.getByRole("region", { name: "Databases" });
    expect(within(databases).getByRole("row", { name: /^shop / })).toHaveFocus();
  });
});
