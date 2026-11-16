import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { Agent, SqlServer } from "../api/types";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { settingsPage } from "./testSettings";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      settings: vi.fn(),
      sqlServers: vi.fn(),
      agents: vi.fn(),
      createSqlServer: vi.fn(),
      updateSqlServer: vi.fn(),
      deleteSqlServer: vi.fn(),
      addAgent: vi.fn(),
      newAgentCode: vi.fn(),
      deleteAgent: vi.fn(),
    },
  };
});

const admin = { id: "1", email: "ana@example.com", displayName: "Ana Admin", role: "Admin" as const, timeZone: null };

const builtIn: Agent = {
  id: "a1",
  name: "Built in",
  placement: "BuiltIn",
  state: "Online",
  version: "0.140.1",
  hostName: "veyra-agent",
  operatingSystem: "Alpine 3.22.2",
  lastSeenAt: new Date().toISOString(),
  error: null,
  servers: 2,
  createdAt: "2026-11-13T08:00:00Z",
  installed: true,
  codeExpiresAt: null,
};

const receiving: SqlServer = {
  id: "s1",
  name: "sql-prod-01",
  address: "sql-prod-01.corp.local",
  port: 1433,
  environment: "production",
  agentId: "a1",
  agentName: "Built in",
  login: "veyra_monitor",
  encryption: "Required",
  state: "Receiving",
  detail: null,
  since: new Date().toISOString(),
  createdAt: "2026-11-13T08:00:00Z",
  changedAt: "2026-11-13T08:00:00Z",
};

const refused: SqlServer = {
  ...receiving,
  id: "s2",
  name: "sql-report-02",
  address: "sql-report-02.corp.local",
  state: "LoginFailed",
  detail: "Login failed for user 'veyra_monitor'.",
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
  vi.mocked(api.currentSession).mockResolvedValue(admin);
  vi.mocked(api.settings).mockResolvedValue(settingsPage);
  vi.mocked(api.sqlServers).mockResolvedValue([receiving, refused]);
  vi.mocked(api.agents).mockResolvedValue([builtIn]);
});

describe("Database monitoring", () => {
  it("shows each server with its state in words and what to do", async () => {
    renderAt("/settings?section=Databases");

    const prod = await screen.findByRole("article", { name: "sql-prod-01" });
    expect(within(prod).getByText("Receiving")).toBeVisible();
    expect(within(prod).getByText(/read by the built-in agent/)).toBeVisible();
    expect(within(prod).getByRole("link", { name: "Open" })).toHaveAttribute(
      "href",
      "/metrics/databases/microsoft.sql_server/sql-prod-01",
    );

    const report = screen.getByRole("article", { name: "sql-report-02" });
    expect(within(report).getByText("Login failed")).toBeVisible();
    expect(within(report).getByText(/SQL Server refused the login veyra_monitor/)).toBeVisible();
    expect(within(report).getByRole("button", { name: "Fix" })).toBeVisible();
  });

  it("counts the servers that need a look in the section list", async () => {
    renderAt("/settings?section=Databases");

    const sections = await screen.findByRole("navigation", { name: "Settings sections" });

    await waitFor(() =>
      expect(within(sections).getByRole("link", { name: /Database monitoring/ })).toHaveTextContent(
        "2 SQL Servers · 1 needs a look",
      ),
    );
    expect(within(sections).getByRole("link", { name: /Agents/ })).toHaveTextContent("1 of 1 online");
  });

  it("adds a server with a password made here, the login script to copy, and then follows the check", async () => {
    const user = userEvent.setup();
    const added: SqlServer = {
      ...receiving,
      id: "s3",
      name: "sql-branch-03",
      address: "10.20.0.14",
      state: "WaitingForData",
      since: null,
      changedAt: new Date().toISOString(),
    };
    vi.mocked(api.createSqlServer).mockResolvedValue(added);
    renderAt("/settings?section=Databases");

    await user.click(await screen.findByRole("button", { name: "+ Add SQL Server" }));
    const panel = screen.getByRole("dialog", { name: "Add a SQL Server" });
    await user.type(within(panel).getByRole("textbox", { name: /Name in Veyra/ }), "sql-branch-03");
    await user.type(within(panel).getByRole("textbox", { name: "Address" }), "10.20.0.14");
    await user.click(within(panel).getByRole("radio", { name: "Trust its certificate" }));

    // The script shows the password hidden, and copies it in full.
    expect(within(panel).getByLabelText("Login script")).toHaveTextContent("CREATE LOGIN [veyra_monitor]");
    expect(within(panel).getByLabelText("Login script")).toHaveTextContent("••••••••••••");
    await user.click(within(panel).getByRole("button", { name: "Copy" }));
    // user-event puts its own clipboard in place, which reads back what was written.
    const copied = await navigator.clipboard.readText();
    expect(copied).toMatch(/WITH PASSWORD = N'[A-Za-z0-9]{24}';/);

    await user.click(within(panel).getByRole("button", { name: "Save and start watching" }));

    const input = vi.mocked(api.createSqlServer).mock.calls[0]?.[0];
    expect(input).toMatchObject({
      name: "sql-branch-03",
      address: "10.20.0.14",
      port: 1433,
      environment: "production",
      agentId: "a1",
      login: "veyra_monitor",
      encryption: "TrustServerCertificate",
    });
    expect(copied).toContain(`N'${input?.password}'`);
    expect(await within(panel).findByRole("status")).toHaveTextContent("Waiting for first data");
    expect(within(panel).getByText(/Logged in as veyra_monitor/)).toHaveTextContent("not known yet");
  });

  it("changes a server without sending a password unless a new one is made", async () => {
    const user = userEvent.setup();
    vi.mocked(api.updateSqlServer).mockResolvedValue(refused);
    renderAt("/settings?section=Databases");

    await user.click(
      within(await screen.findByRole("article", { name: "sql-report-02" })).getByRole("button", { name: "Fix" }),
    );
    const panel = screen.getByRole("dialog", { name: "Change sql-report-02" });
    expect(within(panel).getByText(/The stored password stays/)).toBeVisible();
    await user.click(within(panel).getByRole("button", { name: "Save changes" }));

    expect(vi.mocked(api.updateSqlServer).mock.calls[0]?.[1].password).toBeNull();
  });

  it("removes a server after a second click", async () => {
    const user = userEvent.setup();
    vi.mocked(api.deleteSqlServer).mockResolvedValue(undefined);
    renderAt("/settings?section=Databases");

    const card = await screen.findByRole("article", { name: "sql-prod-01" });
    await user.click(within(card).getByRole("button", { name: "Remove" }));
    await user.click(within(card).getByRole("button", { name: "Remove sql-prod-01" }));

    expect(api.deleteSqlServer).toHaveBeenCalledWith("s1");
  });
});

describe("The agent's state on Database monitoring", () => {
  it("says quietly which agent reads the servers when it is online", async () => {
    renderAt("/settings?section=Databases");

    const strip = await screen.findByRole("status", { name: "Agent" });
    expect(strip).toHaveTextContent("Read by the built-in agent · Online · version 0.140.1");
    expect(within(strip).getByRole("link", { name: "Agents ›" })).toHaveAttribute("href", "/settings?section=Agents");
    expect(screen.getByRole("button", { name: "+ Add SQL Server" })).toBeEnabled();
  });

  it("warns, but still lets servers be added, when the agent has not connected", async () => {
    vi.mocked(api.agents).mockResolvedValue([{ ...builtIn, state: "NotConnected", lastSeenAt: null, version: null }]);
    renderAt("/settings?section=Databases");

    const strip = await screen.findByRole("status", { name: "Agent" });
    expect(strip).toHaveTextContent("The built-in agent is not running yet");
    expect(strip).toHaveTextContent("Servers added now wait for it");
    expect(within(strip).getByRole("link", { name: "How to start it ›" })).toHaveAttribute(
      "href",
      "/settings?section=Agents",
    );
    expect(screen.getByRole("button", { name: "+ Add SQL Server" })).toBeEnabled();
  });

  it("gives the Collector's reason when the agent refused its settings", async () => {
    vi.mocked(api.agents).mockResolvedValue([
      { ...builtIn, state: "ConfigurationFailed", error: 'unknown type: "sqlserverx"' },
    ]);
    renderAt("/settings?section=Databases");

    const strip = await screen.findByRole("status", { name: "Agent" });
    expect(strip).toHaveTextContent("The built-in agent refused its settings");
    expect(strip).toHaveTextContent('unknown type: "sqlserverx"');
  });

  it("explains that the built-in agent is needed, and keeps Add closed, when there is no agent", async () => {
    vi.mocked(api.agents).mockResolvedValue([]);
    vi.mocked(api.sqlServers).mockResolvedValue([]);
    renderAt("/settings?section=Databases");

    const strip = await screen.findByRole("status", { name: "Agent" });
    expect(strip).toHaveTextContent("Veyra has no agent to read database servers");
    expect(strip).toHaveTextContent("deploy/docker-compose.yml");
    const add = screen.getByRole("button", { name: "+ Add SQL Server" });
    expect(add).toBeDisabled();
    expect(add).toHaveAccessibleDescription("Needs an agent first");
  });
});

describe("Agents", () => {
  it("shows the built-in agent with where it runs and its version", async () => {
    renderAt("/settings?section=Agents");

    const agent = await screen.findByRole("article", { name: "Built-in agent" });
    expect(within(agent).getByText("Online")).toBeVisible();
    expect(
      within(agent).getByText(/Beside Veyra on veyra-agent · reads 2 SQL Servers · version 0.140.1/),
    ).toBeVisible();
  });

  it("says what to check when the built-in agent never connected", async () => {
    vi.mocked(api.agents).mockResolvedValue([{ ...builtIn, state: "NotConnected", lastSeenAt: null, version: null }]);
    renderAt("/settings?section=Agents");

    const agent = await screen.findByRole("article", { name: "Built-in agent" });
    expect(within(agent).getByText("Not connected yet")).toBeVisible();
    expect(within(agent).getByText(/check that the agent container runs/)).toBeVisible();
  });

  it("explains how the built-in agent runs, folded away while it is online", async () => {
    const user = userEvent.setup();
    renderAt("/settings?section=Agents");

    const guide = await screen.findByRole("group", { name: "How the built-in agent runs" });
    expect(guide).not.toHaveAttribute("open");

    await user.click(within(guide).getByText("How the built-in agent runs"));

    expect(guide).toHaveAttribute("open");
    expect(within(guide).getByText(/comes with Veyra's Docker Compose deployment/)).toBeVisible();
    expect(within(guide).getByLabelText("Check that it runs")).toHaveTextContent(
      "docker compose -f deploy/docker-compose.yml ps agent",
    );
  });

  it("opens the explanation, with the command to start it, when the agent has not connected", async () => {
    const user = userEvent.setup();
    vi.mocked(api.agents).mockResolvedValue([{ ...builtIn, state: "NotConnected", lastSeenAt: null, version: null }]);
    renderAt("/settings?section=Agents");

    const guide = await screen.findByRole("group", { name: "How the built-in agent runs" });
    expect(guide).toHaveAttribute("open");
    const start = within(guide).getByLabelText("Start it");
    expect(start).toHaveTextContent("docker compose -f deploy/docker-compose.yml up -d --no-deps agent");

    await user.click(within(guide).getByRole("button", { name: "Copy: Start it" }));

    expect(await navigator.clipboard.readText()).toBe(
      "docker compose -f deploy/docker-compose.yml up -d --no-deps agent",
    );
  });

  it("opens the explanation when there is no agent at all", async () => {
    vi.mocked(api.agents).mockResolvedValue([]);
    renderAt("/settings?section=Agents");

    const guide = await screen.findByRole("group", { name: "How the built-in agent runs" });
    expect(guide).toHaveAttribute("open");
    expect(within(guide).getByLabelText("Start Veyra with its agent")).toHaveTextContent(
      "docker compose -f deploy/docker-compose.yml up -d --build",
    );
  });
});

describe("Agents in other networks", () => {
  const remote: Agent = {
    ...builtIn,
    id: "a2",
    name: "Company network",
    placement: "Remote",
    state: "NotConnected",
    version: null,
    hostName: null,
    operatingSystem: null,
    lastSeenAt: null,
    servers: 0,
    installed: false,
    codeExpiresAt: null,
  };

  it("adds an agent and shows its one-time code with the line that installs it on Windows or Linux", async () => {
    const user = userEvent.setup();
    vi.mocked(api.addAgent).mockResolvedValue({
      agent: remote,
      code: "K7QM-4TWX-9PRA",
      expiresAt: "2026-11-13T19:23:00Z",
    });
    renderAt("/settings?section=Agents");

    await user.click(await screen.findByRole("button", { name: "Add an agent" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Company network");
    await user.click(screen.getByRole("button", { name: "Add agent" }));

    const box = await screen.findByRole("region", { name: "Install Company network" });
    expect(within(box).getByText("K7QM-4TWX-9PRA")).toBeVisible();
    expect(within(box).getByRole("tab", { name: "Windows" })).toHaveAttribute("aria-selected", "true");
    expect(within(box).getByLabelText("Install command")).toHaveTextContent(
      `irm ${window.location.origin}/v1/agents/install.ps1 | iex; Install-VeyraAgent -Code K7QM-4TWX-9PRA`,
    );
    expect(within(box).getByRole("status")).toHaveTextContent("Waiting for Company network to connect");

    await user.click(within(box).getByRole("tab", { name: "Linux" }));

    expect(within(box).getByLabelText("Install command")).toHaveTextContent(
      `curl -fsSL ${window.location.origin}/v1/agents/install.sh | sudo sh -s -- --code K7QM-4TWX-9PRA`,
    );
    expect(api.addAgent).toHaveBeenCalledWith("Company network");
  });

  it("says when the new agent has connected", async () => {
    const user = userEvent.setup();
    vi.mocked(api.addAgent).mockResolvedValue({
      agent: remote,
      code: "K7QM-4TWX-9PRA",
      expiresAt: "2026-11-13T19:23:00Z",
    });
    vi.mocked(api.agents)
      .mockResolvedValueOnce([builtIn])
      .mockResolvedValue([builtIn, { ...remote, state: "Online", installed: true, version: "0.140.1" }]);
    renderAt("/settings?section=Agents");

    await user.click(await screen.findByRole("button", { name: "Add an agent" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Company network");
    await user.click(screen.getByRole("button", { name: "Add agent" }));

    const box = await screen.findByRole("region", { name: "Install Company network" });
    await waitFor(() => expect(within(box).getByRole("status")).toHaveTextContent("Company network is connected"));
  });

  it("gives an agent that is not installed yet a new code", async () => {
    const user = userEvent.setup();
    vi.mocked(api.agents).mockResolvedValue([builtIn, remote]);
    vi.mocked(api.newAgentCode).mockResolvedValue({ code: "ABCD-EFGH-JKLM", expiresAt: "2026-11-13T19:23:00Z" });
    renderAt("/settings?section=Agents");

    const card = await screen.findByRole("article", { name: "Company network" });
    expect(within(card).getByText("Not installed yet")).toBeVisible();
    await user.click(within(card).getByRole("button", { name: "New code" }));

    const box = await screen.findByRole("region", { name: "Install Company network" });
    expect(within(box).getByText("ABCD-EFGH-JKLM")).toBeVisible();
    expect(api.newAgentCode).toHaveBeenCalledWith("a2");
  });

  it("removes an agent after a second click, with the lines that uninstall it", async () => {
    const user = userEvent.setup();
    vi.mocked(api.agents).mockResolvedValue([builtIn, { ...remote, state: "Online", installed: true }]);
    vi.mocked(api.deleteAgent).mockResolvedValue(undefined);
    renderAt("/settings?section=Agents");

    const card = await screen.findByRole("article", { name: "Company network" });
    await user.click(within(card).getByRole("button", { name: "Remove" }));

    expect(within(card).getByLabelText("Uninstall on Windows")).toHaveTextContent("Uninstall-VeyraAgent");
    expect(within(card).getByLabelText("Uninstall on Linux")).toHaveTextContent("--uninstall");
    await user.click(within(card).getByRole("button", { name: "Remove Company network" }));

    expect(api.deleteAgent).toHaveBeenCalledWith("a2");
  });

  it("offers no removal for the built-in agent", async () => {
    renderAt("/settings?section=Agents");

    const card = await screen.findByRole("article", { name: "Built-in agent" });
    expect(within(card).queryByRole("button", { name: "Remove" })).toBeNull();
  });
});
