import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { historySummary } from "../alerts/testAlertHistory";
import type * as client from "../api/client";
import { api } from "../api/client";
import type { HomeSnapshot, HostCard, ServiceCard } from "../api/types";
import { stormSnapshot } from "../home/testSnapshots";
import { overview } from "../metrics/testMetrics";
import { SessionProvider } from "../session/SessionProvider";
import { setDisplayZone } from "../time/zone";
import { App } from "./App";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      searchLogs: vi.fn(),
      summarizeLogs: vi.fn(),
      metricsOverview: vi.fn(),
      setTimeZone: vi.fn(),
      alerts: vi.fn(),
      alertSummary: vi.fn(),
    },
  };
});

const viewer = {
  id: "1",
  email: "viewer@example.com",
  displayName: "Dana Kim",
  role: "Viewer" as const,
  timeZone: "UTC" as string | null,
};

/** One error at 07:34:12.418 UTC. */
const logEntry = {
  id: 1,
  timestamp: "2026-11-09T07:34:12.418Z",
  observedTimestamp: "2026-11-09T07:34:12.500Z",
  severity: "Error" as const,
  severityText: "ERROR",
  body: "Payment declined",
  serviceName: "payments-gateway",
  environment: "production",
  traceId: null,
  spanId: null,
  attributes: {},
  resourceAttributes: {},
};

/** Two environments: production in a storm, staging clear. */
const snapshot: HomeSnapshot = {
  ...stormSnapshot,
  environments: [
    { name: "production", condition: "Storm", errorRatio: 0.058 },
    { name: "staging", condition: "Clear", errorRatio: 0.001 },
  ],
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
  localStorage.clear();
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.home).mockResolvedValue(snapshot);
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [], nextCursor: null });
  vi.mocked(api.summarizeLogs).mockResolvedValue({ bucketSeconds: 120, buckets: [], services: [] });
  vi.mocked(api.metricsOverview).mockResolvedValue(overview);
  vi.mocked(api.alerts).mockResolvedValue({ items: [], next: null });
  vi.mocked(api.alertSummary).mockResolvedValue(historySummary);
});

describe("The sidebar", () => {
  it.each(["/", "/logs", "/metrics", "/alerts"])("shows the chosen environment and its weather on %s", async (path) => {
    renderAt(path);

    const main = await screen.findByRole("navigation", { name: "Main" });
    expect(
      await within(main).findByRole("button", { name: /^Environment: production Storm · 5.8% errors/ }),
    ).toBeVisible();
  });

  it("leads home from the logo", async () => {
    renderAt("/logs");

    expect(await screen.findByRole("link", { name: "Veyra home" })).toHaveAttribute("href", "/");
  });

  it("counts the open alerts beside Alerts, in place of the master lights", async () => {
    renderAt("/logs");

    const alerts = await screen.findByRole("link", { name: "Alerts" });
    await waitFor(() => expect(alerts).toHaveAccessibleDescription("1 warning, 1 caution"));
    expect(screen.queryByRole("status", { name: "Master warning" })).not.toBeInTheDocument();
  });

  it("marks the current screen with its own colour", async () => {
    renderAt("/metrics");

    const metrics = await screen.findByRole("link", { name: "Metrics" });
    expect(metrics).toHaveAttribute("aria-current", "page");
    expect(metrics).toHaveClass("sb-metrics");
  });

  it("collapses to icons, keeps the names for screen readers, and remembers the choice", async () => {
    const user = userEvent.setup();
    const { container } = renderAt("/");

    await user.click(await screen.findByRole("button", { name: "Collapse the sidebar" }));

    expect(container.querySelector(".shell")).toHaveAttribute("data-sidebar", "mini");
    expect(screen.getByRole("link", { name: "Logs" })).toBeInTheDocument();
    expect(localStorage.getItem("veyra.sidebar")).toBe("mini");

    await user.click(screen.getByRole("button", { name: "Expand the sidebar" }));

    expect(container.querySelector(".shell")).toHaveAttribute("data-sidebar", "full");
  });

  it("opens the account menu with the account, the theme, and signing out", async () => {
    const user = userEvent.setup();
    vi.mocked(api.signOut).mockResolvedValue(undefined);
    renderAt("/");

    await user.click(await screen.findByRole("button", { name: /Dana Kim/ }));
    const menu = screen.getByRole("dialog", { name: "Account" });
    expect(within(menu).getByRole("link", { name: "Your account" })).toHaveAttribute("href", "/account");

    await user.click(within(menu).getByRole("button", { name: "Dark" }));
    expect(document.documentElement).toHaveAttribute("data-theme", "dark");

    await user.click(within(menu).getByRole("button", { name: "Sign out" }));
    expect(api.signOut).toHaveBeenCalled();
  });

  it("closes a menu with Escape and gives the focus back to its button", async () => {
    const user = userEvent.setup();
    renderAt("/");

    const button = await screen.findByRole("button", { name: /Dana Kim/ });
    await user.click(button);
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog", { name: "Account" })).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it("shows settings to administrators only", async () => {
    renderAt("/");

    await screen.findByRole("navigation", { name: "Main" });
    expect(screen.queryByRole("link", { name: "Ingestion keys" })).not.toBeInTheDocument();
  });

  it("paints the chosen environment's weather on every screen, calm outside the home and alerts", async () => {
    const { container } = renderAt("/logs");

    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-storm"));
    expect(container.querySelector(".shell")).toHaveClass("calm");
  });
});

describe("Times in the person's zone (VEY-22)", () => {
  beforeEach(() => {
    // jsdom lays nothing out; the virtual log list needs a viewport size to draw its rows.
    vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(800);
    vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(1000);
    vi.mocked(api.searchLogs).mockResolvedValue({ items: [logEntry], nextCursor: null });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    setDisplayZone("UTC");
  });

  it("shows times in the zone kept on the account", async () => {
    vi.mocked(api.currentSession).mockResolvedValue({ ...viewer, timeZone: "Asia/Tokyo" });
    renderAt("/logs");

    expect(await screen.findByText("Time (GMT+9)")).toBeInTheDocument();
    expect(await screen.findByText("16:34:12.418")).toHaveAttribute("title", "2026-11-09 07:34:12.418 UTC");
  });

  it("uses the browser's zone until the person picks one", async () => {
    // The test runner's zone stands in for the browser's.
    vi.stubEnv("TZ", "America/New_York");
    vi.mocked(api.currentSession).mockResolvedValue({ ...viewer, timeZone: null });
    renderAt("/logs");

    expect(await screen.findByText("Time (EST)")).toBeInTheDocument();
    expect(document.querySelector(".shell")).toHaveAttribute("data-zone", "America/New_York");
  });

  it("switches every screen to UTC from the account menu, and remembers it in this browser", async () => {
    const user = userEvent.setup();
    vi.mocked(api.currentSession).mockResolvedValue({ ...viewer, timeZone: "Asia/Tokyo" });
    renderAt("/logs");
    await screen.findByText("Time (GMT+9)");

    await user.click(screen.getByRole("button", { name: /Dana Kim/ }));
    const times = within(screen.getByRole("dialog", { name: "Account" })).getByRole("group", { name: "Times" });
    await user.click(within(times).getByRole("button", { name: "UTC" }));

    expect(await screen.findByText("Time (UTC)")).toBeInTheDocument();
    expect(localStorage.getItem("veyra.times")).toBe("utc");
    expect(within(times).getByRole("button", { name: "Tokyo" })).toHaveAttribute("aria-pressed", "false");
  });
});

describe("The sidebar on phones", () => {
  beforeEach(() => {
    phoneWidth();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("waits in a drawer behind the menu button, and Escape closes it", async () => {
    const user = userEvent.setup();
    renderAt("/");

    const menu = await screen.findByRole("button", { name: "Menu" });
    const sidebar = screen.getByRole("navigation", { name: "Main", hidden: true });
    expect(sidebar).toHaveAttribute("inert");

    await user.click(menu);
    expect(sidebar).not.toHaveAttribute("inert");
    expect(screen.getByRole("button", { name: "Close the menu" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(sidebar).toHaveAttribute("inert");
    expect(menu).toHaveFocus();
  });

  it("closes the drawer once a screen is chosen", async () => {
    const user = userEvent.setup();
    renderAt("/");

    await user.click(await screen.findByRole("button", { name: "Menu" }));
    await user.click(screen.getByRole("link", { name: "Logs" }));

    expect(await screen.findByRole("heading", { name: "Logs" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Main", hidden: true })).toHaveAttribute("inert");
  });
});

/** A phone-sized window: only the queries of narrow screens match. */
function phoneWidth() {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query.includes("max-width"),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

/** Opens the environment switcher in the sidebar and picks an environment. */
async function chooseEnvironment(user: ReturnType<typeof userEvent.setup>, name: RegExp) {
  await user.click(await screen.findByRole("button", { name: /^Environment:/ }));
  await user.click(within(screen.getByRole("dialog", { name: "Choose an environment" })).getByRole("button", { name }));
}

describe("The chosen environment", () => {
  it("narrows the Log Explorer, and a new choice searches again", async () => {
    const user = userEvent.setup();
    renderAt("/logs");
    await waitFor(() => expect(lastParams(api.searchLogs).get("environment")).toBe("production"));

    await chooseEnvironment(user, /staging/);

    await waitFor(() => expect(lastParams(api.searchLogs).get("environment")).toBe("staging"));
    expect(lastParams(api.summarizeLogs).get("environment")).toBe("staging");
  });

  it("finds an environment by name", async () => {
    const user = userEvent.setup();
    renderAt("/");

    await user.click(await screen.findByRole("button", { name: /^Environment:/ }));
    await user.type(screen.getByRole("searchbox", { name: "Find an environment" }), "stag");

    const dialog = screen.getByRole("dialog", { name: "Choose an environment" });
    expect(
      within(dialog)
        .getAllByRole("button")
        .map((button) => button.textContent),
    ).toEqual([expect.stringContaining("staging")]);
  });

  it("narrows the alerts and their counts", async () => {
    const user = userEvent.setup();
    renderAt("/alerts");
    await chooseEnvironment(user, /staging/);

    await waitFor(() =>
      expect(api.alerts).toHaveBeenLastCalledWith(
        expect.objectContaining({ environment: "staging" }),
        null,
        expect.anything(),
      ),
    );
    expect(api.alertSummary).toHaveBeenLastCalledWith(30, "staging", expect.anything());
  });

  it("narrows the metric cards to the environment, keeping hosts that name none", async () => {
    const user = userEvent.setup();
    const [firstHost] = overview.hosts as [HostCard];
    const [firstService] = overview.services as [ServiceCard];
    vi.mocked(api.metricsOverview).mockResolvedValue({
      ...overview,
      hosts: [...overview.hosts, { ...firstHost, name: "shared-01", environment: null }],
      services: [...overview.services, { ...firstService, name: "staging-api", environment: "staging" }],
    });
    renderAt("/metrics");
    expect(await screen.findByRole("heading", { name: "payments-gateway" })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "staging-api" })).not.toBeInTheDocument();

    await chooseEnvironment(user, /staging/);

    expect(await screen.findByRole("heading", { name: "staging-api" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "shared-01" })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "payments-gateway" })).not.toBeInTheDocument();
  });
});

function lastParams(mock: unknown): URLSearchParams {
  const calls = vi.mocked(mock as (params: URLSearchParams) => unknown).mock.calls;

  return calls.at(-1)?.[0] ?? new URLSearchParams();
}
