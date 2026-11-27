import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { historySummary } from "../../alerts/testAlertHistory";
import type * as client from "../../api/client";
import { api } from "../../api/client";
import type { Role } from "../../api/types";
import { App } from "../../app/App";
import { environmentChoicesOf, stormSnapshot } from "../../home/testSnapshots";
import { overview } from "../../metrics/testMetrics";
import { SessionProvider } from "../../session/SessionProvider";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      homeEnvironments: vi.fn(),
      searchLogs: vi.fn(),
      summarizeLogs: vi.fn(),
      metricsOverview: vi.fn(),
      alerts: vi.fn(),
      alertSummary: vi.fn(),
      agentsSummary: vi.fn(),
      about: vi.fn(),
      releases: vi.fn(),
      search: vi.fn(),
      savedViews: vi.fn(),
      parseLogQuery: vi.fn(),
      databaseOverview: vi.fn(),
      databaseBackups: vi.fn(),
    },
  };
});

/** A person who picked Console. */
function sessionAs(role: Role) {
  return {
    id: "1",
    email: "dana@example.com",
    displayName: "Dana Kim",
    role,
    timeZone: "UTC",
    look: { chosen: "console", default: "forecast", mayChoose: true },
  };
}

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
  localStorage.clear();
  vi.mocked(api.currentSession).mockResolvedValue(sessionAs("Viewer"));
  vi.mocked(api.home).mockResolvedValue(stormSnapshot);
  vi.mocked(api.homeEnvironments).mockImplementation((search) =>
    Promise.resolve(environmentChoicesOf(stormSnapshot.environments, search)),
  );
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [], nextCursor: null });
  vi.mocked(api.summarizeLogs).mockResolvedValue({
    bucketSeconds: 120,
    buckets: [],
    services: [],
    total: 0,
    errors: 0,
  });
  vi.mocked(api.metricsOverview).mockResolvedValue(overview);
  vi.mocked(api.alerts).mockResolvedValue({ items: [], next: null });
  vi.mocked(api.alertSummary).mockResolvedValue(historySummary);
  vi.mocked(api.savedViews).mockResolvedValue([]);
  vi.mocked(api.about).mockResolvedValue({ version: "0.4.1", build: "aaaaaaaaaaaa" });
  vi.mocked(api.releases).mockResolvedValue([
    {
      version: "0.4.1",
      date: "2026-11-27",
      summary: "Drill-in tiles and release notes.",
      sections: [{ title: "Added", items: ["Tiles open their details."] }],
    },
  ]);
  vi.mocked(api.parseLogQuery).mockResolvedValue({ terms: [] });
  // The server screen's reads stay pending: the menu is what these tests look at.
  vi.mocked(api.databaseOverview).mockReturnValue(new Promise(() => undefined));
  vi.mocked(api.databaseBackups).mockReturnValue(new Promise(() => undefined));
  vi.mocked(api.search).mockImplementation((query) =>
    Promise.resolve({
      query,
      services: {
        items: [{ kind: "service", name: "Payments.API", health: "Caution", detail: "742 ms p95" }],
        partial: false,
      },
      hosts: { items: [], partial: false },
      databases: {
        items: [
          {
            kind: "database",
            name: "PaymentsDB",
            engine: "microsoft.sql_server",
            server: "SQL-PROD-01",
            detail: "22 GB",
          },
        ],
        partial: false,
      },
      traces: { items: [], partial: false },
      logs: { count: 4812, partial: false },
    }),
  );
  vi.mocked(api.agentsSummary).mockResolvedValue({
    sqlServers: 3,
    postgresServers: 1,
    needLook: 0,
    agents: 4,
    agentsOnline: 3,
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("The Console shell", () => {
  it("Shell_PersonChoseConsole_GroupsTheMenuByWhatPeopleDo", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(sessionAs("Admin"));
    renderAt("/logs");

    const main = await screen.findByRole("navigation", { name: "Main" });
    const groups = within(main)
      .getAllByRole("region")
      .map((group) => group.getAttribute("aria-labelledby"));

    expect(groups).toEqual(["cn-group-observe", "cn-group-resources", "cn-group-respond", "cn-group-manage"]);
    expect(within(main).getByRole("link", { name: "Overview" })).toHaveAttribute("href", "/");
    expect(within(main).getByRole("link", { name: "Logs" })).toHaveClass("active");
    expect(within(main).getByRole("link", { name: "Settings" })).toHaveAttribute("href", "/settings");
  });

  it("Shell_Admin_SaysAtTheFootHowManyAgentsAreConnected", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(sessionAs("Admin"));
    renderAt("/logs");

    const main = await screen.findByRole("navigation", { name: "Main" });

    expect(await within(main).findByText("3 of 4 agents connected")).toBeVisible();
    expect(within(main).getByText("Connected")).toBeVisible();
  });

  it("Shell_Version_AtTheFootOpensTheReleaseNotes", async () => {
    renderAt("/logs");

    const main = await screen.findByRole("navigation", { name: "Main" });
    const version = await within(main).findByRole("button", { name: "Veyra 0.4.1: what's new" });
    expect(within(main).queryByText("Self-hosted")).not.toBeInTheDocument();

    await userEvent.click(version);

    const notes = await screen.findByRole("dialog", { name: "What's new in Veyra" });
    expect(await within(notes).findByText("Tiles open their details.")).toBeVisible();
    expect(within(notes).getByText("in use here")).toBeVisible();
  });

  it("Shell_Viewer_HasNoManageGroupAndAsksNothingOfTheAgents", async () => {
    renderAt("/");

    const main = await screen.findByRole("navigation", { name: "Main" });

    expect(within(main).queryByText("Manage")).not.toBeInTheDocument();
    expect(within(main).queryByRole("link", { name: "Settings" })).not.toBeInTheDocument();
    expect(api.agentsSummary).not.toHaveBeenCalled();
  });

  it("Shell_Search_ShowsWhatTheServerFoundInGroups", async () => {
    renderAt("/");

    await userEvent.type(await screen.findByRole("combobox", { name: /^Search logs, traces/ }), "pay");

    const results = await screen.findByRole("listbox", { name: "Search results" });
    expect(await within(results).findByRole("group", { name: "Services" })).toHaveTextContent("Payments.API742 ms p95");
    expect(within(results).getByRole("group", { name: "Databases" })).toHaveTextContent("PaymentsDB on SQL-PROD-01");
    expect(within(results).getByRole("group", { name: "Logs" })).toHaveTextContent("4,812 in the range");
    // The server is asked for the words, in the chosen environment and the shared range.
    expect(vi.mocked(api.search).mock.calls.at(-1)?.[0]).toBe("pay");
  });

  it("Shell_SearchWords_OpenTheLogExplorerWithThem", async () => {
    renderAt("/");

    const search = await screen.findByRole("combobox", { name: /^Search logs, traces/ });
    await userEvent.type(search, "timed out{Enter}");

    // The Log explorer opens with the words as a query, which it sends to the server (ADR 0024).
    expect(await screen.findByDisplayValue('"timed out"')).toBeVisible();
    expect(search).toHaveValue("");
  });

  it("Shell_OpenAlerts_CountsThemOnTheBellAndInWords", async () => {
    renderAt("/logs");

    const bell = await screen.findByRole("link", { name: "Open alerts", description: /warning/ });

    expect(bell).toHaveAttribute("href", "/alerts");
    expect(bell).toHaveTextContent(String(stormSnapshot.counts.warning + stormSnapshot.counts.caution));
  });

  it("Shell_WhileDrawn_SwitchesThePageToConsoleColours", async () => {
    const { unmount } = renderAt("/logs");

    await screen.findByRole("navigation", { name: "Main" });
    expect(document.documentElement.dataset.look).toBe("console");

    unmount();
    expect(document.documentElement.dataset.look).toBeUndefined();
  });

  it("Shell_AccountMenu_OffersAccountThemeAndSignOut", async () => {
    vi.mocked(api.signOut).mockResolvedValue(undefined);
    renderAt("/logs");

    await userEvent.click(await screen.findByRole("button", { name: "Account: Dana Kim" }));
    const menu = screen.getByRole("dialog", { name: "Account" });

    expect(within(menu).getByRole("link", { name: "Your account and look" })).toHaveAttribute("href", "/account");
    await userEvent.click(within(menu).getByRole("button", { name: "Dark" }));
    expect(document.documentElement.dataset.theme).toBe("dark");

    await userEvent.click(within(menu).getByRole("button", { name: "Sign out" }));
    await waitFor(() => expect(api.signOut).toHaveBeenCalled());
  });

  it("Shell_Phone_OpensTheMenuAsADrawerAndClosesItWithEscape", async () => {
    vi.stubGlobal("matchMedia", (query: string) => ({
      matches: false,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    renderAt("/logs");

    const menuButton = await screen.findByRole("button", { name: "Menu" });
    const main = screen.getByRole("navigation", { name: "Main" });
    expect(main).toHaveAttribute("inert");

    await userEvent.click(menuButton);
    expect(main).not.toHaveAttribute("inert");
    expect(main).toHaveClass("open");
    expect(screen.getByRole("button", { name: "Close the menu" })).toHaveFocus();

    await userEvent.keyboard("{Escape}");
    expect(menuButton).toHaveFocus();
    expect(menuButton).toHaveAttribute("aria-expanded", "false");
  });
  it("Shell_ScreenWithARange_LeavesTheRangeToTheTopBar", async () => {
    renderAt("/metrics");

    await screen.findByRole("heading", { level: 1, name: "Metrics" });
    expect(screen.queryByRole("group", { name: "Time range" })).not.toBeInTheDocument();
  });

  it("Shell_ScreenWithViews_PutsSimpleAndAdvancedInTheTopBar", async () => {
    renderAt("/logs");

    // jsdom also counts the panels' headers as banners; the top bar comes first.
    const [top] = await screen.findAllByRole("banner");
    expect(await within(top as HTMLElement).findByRole("button", { name: "Simple view" })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /^(Simple|Advanced) view$/ })).toHaveLength(1);
    expect(screen.queryByRole("group", { name: "View" })).not.toBeInTheDocument();
  });

  it("Shell_ViewLens_ExplainsEachViewAndSwitchesForEveryScreen", async () => {
    renderAt("/logs");

    await userEvent.click(await screen.findByRole("button", { name: "Simple view" }));
    const menu = screen.getByRole("menu", { name: "View" });
    expect(within(menu).getByRole("menuitemradio", { name: /^Simple/ })).toHaveAttribute("aria-checked", "true");
    expect(within(menu).getByText(/Every counter, wait, column, and setting/)).toBeVisible();

    await userEvent.click(within(menu).getByRole("menuitemradio", { name: /^Advanced/ }));

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Advanced view" })).toHaveFocus();
    localStorage.removeItem("veyra.view");
  });

  it("Shell_PhoneScreenWithViews_KeepsSimpleAndAdvancedInTheScreenBar", async () => {
    vi.stubGlobal("matchMedia", (query: string) => ({
      matches: false,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    renderAt("/logs");

    const toggle = await screen.findByRole("button", { name: /^(Simple|Advanced) view$/ });
    expect(
      within(screen.getAllByRole("banner")[0] as HTMLElement).queryByRole("button", {
        name: /^(Simple|Advanced) view$/,
      }),
    ).not.toBeInTheDocument();
    expect(toggle.closest(".screen-bar")).not.toBeNull();
  });

  it("Shell_Alerts_FollowTheTopBarRangeInWholeDays", async () => {
    renderAt("/alerts?range=7d");

    await waitFor(() => expect(api.alertSummary).toHaveBeenCalledWith(7, null, expect.anything()));
    expect(screen.queryByRole("group", { name: "Period" })).not.toBeInTheDocument();
  });
  it("Shell_ServerPage_MarksDatabasesInTheMenuNotMetrics", async () => {
    renderAt("/metrics/databases/microsoft.sql_server/sql-prod-01");

    const main = await screen.findByRole("navigation", { name: "Main" });
    expect(within(main).getByRole("link", { name: "Databases" })).toHaveAttribute("aria-current", "page");
    expect(within(main).getByRole("link", { name: "Metrics" })).not.toHaveAttribute("aria-current");
  });
});
