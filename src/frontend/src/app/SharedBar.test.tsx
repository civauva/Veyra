import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { historySummary } from "../alerts/testAlertHistory";
import type * as client from "../api/client";
import { api } from "../api/client";
import type { HomeSnapshot, HostCard, ServiceCard } from "../api/types";
import { stormSnapshot } from "../home/testSnapshots";
import { overview } from "../metrics/testMetrics";
import { SessionProvider } from "../session/SessionProvider";
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
      alerts: vi.fn(),
      alertSummary: vi.fn(),
    },
  };
});

const viewer = { id: "1", email: "viewer@example.com", displayName: "Dana Kim", role: "Viewer" as const };

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

describe("The top bar", () => {
  it.each(["/", "/logs", "/metrics", "/alerts"])(
    "shows the environments, master lights, and clock on %s",
    async (path) => {
      renderAt(path);

      const environments = await screen.findByRole("group", { name: "Environment" });
      expect(within(environments).getByRole("button", { name: /staging/ })).toBeVisible();
      expect(screen.getByText("07:34")).toBeVisible();
      expect(screen.getByRole("status", { name: /warning/ })).toBeVisible();
    },
  );

  it("paints the chosen environment's weather on every screen, calm outside the home and alerts", async () => {
    const { container } = renderAt("/logs");

    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-storm"));
    expect(container.querySelector(".shell")).toHaveClass("calm");
  });
});

describe("The chosen environment", () => {
  it("narrows the Log Explorer, and a new choice searches again", async () => {
    const user = userEvent.setup();
    renderAt("/logs");
    await waitFor(() => expect(lastParams(api.searchLogs).get("environment")).toBe("production"));

    await user.click(await screen.findByRole("button", { name: /staging/ }));

    await waitFor(() => expect(lastParams(api.searchLogs).get("environment")).toBe("staging"));
    expect(lastParams(api.summarizeLogs).get("environment")).toBe("staging");
  });

  it("narrows the alerts and their counts", async () => {
    const user = userEvent.setup();
    renderAt("/alerts");
    await screen.findByRole("group", { name: "Environment" });

    await user.click(screen.getByRole("button", { name: /staging/ }));

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

    await user.click(screen.getByRole("button", { name: /staging/ }));

    expect(await screen.findByRole("heading", { name: "staging-api" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "shared-01" })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "payments-gateway" })).not.toBeInTheDocument();
  });
});

function lastParams(mock: unknown): URLSearchParams {
  const calls = vi.mocked(mock as (params: URLSearchParams) => unknown).mock.calls;

  return calls.at(-1)?.[0] ?? new URLSearchParams();
}
