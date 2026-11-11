import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import type { HomeAlert, HomeBlock, HomeSnapshot } from "../api/types";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { clearSnapshot, stormSnapshot } from "./testSnapshots";

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
    },
  };
});

const viewer = { id: "1", email: "viewer@example.com", displayName: "Dana Kim", role: "Viewer" as const };

function renderHome() {
  return render(
    <MemoryRouter initialEntries={["/"]}>
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

afterEach(() => {
  vi.useRealTimers();
});

describe("HomeScreen", () => {
  it("reads the storm like a weather report", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);

    renderHome();

    expect(await screen.findByRole("heading", { level: 1, name: "Storm over Services" })).toBeInTheDocument();
    expect(screen.getByText("payments-gateway fails 21.6% of the time since 06:58.", { exact: false })).toBeVisible();
    expect(screen.getByTestId("hero-ratio")).toHaveTextContent("5.8%");
    expect(screen.getByRole("status", { name: "Master warning" })).toHaveTextContent("1 warning");
    expect(screen.getByRole("status", { name: "Master caution" })).toHaveTextContent("1 caution");
  });

  it("lists alerts numbered, warnings first, with the steps of the selected one", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);

    renderHome();

    const pane = await screen.findByRole("region", { name: "Needs you" });
    const alerts = within(pane).getAllByRole("button", { name: /error rate/i });
    expect(alerts.map((alert) => alert.textContent)).toEqual([
      expect.stringMatching(/^1servicepayments-gateway/),
      expect.stringMatching(/^2servicebilling-worker/),
    ]);
    expect(alerts[0]).toHaveAttribute("aria-expanded", "true");
    expect(within(pane).getByRole("link", { name: "Open logs" })).toHaveAttribute(
      "href",
      "/logs?severity=Error&service=payments-gateway",
    );
    expect(within(pane).getByRole("link", { name: "± 1 minute" })).toHaveAttribute(
      "href",
      "/logs?service=payments-gateway&around=2026-11-07T06%3A58%3A00.000Z",
    );
  });

  it("lists a host's disk alert with its tag, its days, and a way to the host's metrics", async () => {
    const [warning, caution] = stormSnapshot.alerts as [HomeAlert, HomeAlert];
    const [firstBlock] = stormSnapshot.blocks as [HomeBlock];
    const disk = {
      ...caution,
      id: "alert-disk",
      block: "host:production/web-01",
      service: null,
      kind: "host" as const,
      name: "web-01",
      part: "/",
      rule: "disk-filling" as const,
      value: 9.2,
      threshold: 14,
      windowSeconds: 86_400,
    };
    vi.mocked(api.home).mockResolvedValue({
      ...stormSnapshot,
      alerts: [warning, disk],
      blocks: [
        ...stormSnapshot.blocks,
        { ...firstBlock, id: "host:production/web-01", kind: "host", name: "web-01", health: "Caution" },
      ],
    });
    const user = userEvent.setup();

    renderHome();

    const pane = await screen.findByRole("region", { name: "Needs you" });
    const row = within(pane).getByRole("button", { name: /Disk filling/ });
    expect(row).toHaveTextContent("hostweb-01 · /");
    expect(row).toHaveTextContent("Disk filling · full within 14 days");
    expect(row).toHaveTextContent("9 days");
    await user.click(row);
    expect(within(pane).getByRole("link", { name: "Open metrics" })).toHaveAttribute("href", "/metrics/hosts/web-01");
    expect(within(pane).getByText(/when a disk is full within 14 days/)).toBeVisible();
    expect(within(pane).getByText(/services · 1 host/)).toBeVisible();
    expect(screen.getByText(/Disk \/ on web-01 is full in 9 days at today's pace\./)).toBeVisible();
  });

  it("notes a host alert under its hour by host and disk", async () => {
    const [warning, caution] = stormSnapshot.alerts as [HomeAlert, HomeAlert];
    const disk = {
      ...caution,
      id: "alert-disk",
      service: null,
      kind: "host" as const,
      name: "web-01",
      part: "/",
      rule: "disk-filling" as const,
      since: "2026-11-07T04:20:00Z",
    };
    vi.mocked(api.home).mockResolvedValue({ ...stormSnapshot, alerts: [warning, disk] });

    renderHome();

    const hours = await screen.findByRole("list", { name: "Hours" });
    expect(hours).toHaveTextContent("04:20 web-01 · /");
    expect(hours).not.toHaveTextContent("null");
  });

  it("says so in the hero when no records arrived, instead of a dash", async () => {
    vi.mocked(api.home).mockResolvedValue({
      ...stormSnapshot,
      condition: { ...stormSnapshot.condition, errorRatio: null },
    });

    renderHome();

    expect(await screen.findByTestId("hero-ratio")).toHaveTextContent("No records");
  });

  it("opens the steps of another alert when it is chosen", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);
    const user = userEvent.setup();

    renderHome();
    const pane = await screen.findByRole("region", { name: "Needs you" });
    await user.click(within(pane).getByRole("button", { name: /billing-worker/ }));

    expect(within(pane).getByRole("button", { name: /billing-worker/ })).toHaveAttribute("aria-expanded", "true");
    expect(within(pane).getByRole("link", { name: "Open logs" })).toHaveAttribute(
      "href",
      "/logs?severity=Error&service=billing-worker",
    );
  });

  it("shows clear skies and nothing to do when no alert is open", async () => {
    vi.mocked(api.home).mockResolvedValue(clearSnapshot);

    renderHome();

    expect(await screen.findByRole("heading", { level: 1, name: "Clear skies" })).toBeInTheDocument();
    expect(screen.getByText("Nothing needs you")).toBeVisible();
    expect(screen.getByRole("status", { name: "Master warning" })).toHaveTextContent("0 warnings");
  });

  it("lists alerts resolved since yesterday with their peak and times", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);

    renderHome();

    const resolved = await screen.findByRole("list", { name: "Resolved since yesterday" });
    expect(resolved).toHaveTextContent("orders-api · error rate 1.2%");
    expect(resolved).toHaveTextContent("03:10–03:24");
  });

  it("draws one dot per service, named for screen readers", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);

    renderHome();

    const dots = await screen.findByRole("list", { name: "All 8 services" });
    expect(within(dots).getAllByRole("listitem")).toHaveLength(8);
    expect(within(dots).getByRole("listitem", { name: "payments-gateway: warning" })).toBeInTheDocument();
  });

  it("shows the latency and the filling disk from metrics", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);

    renderHome();

    const metrics = await screen.findByRole("region", { name: "Key metrics" });
    expect(within(metrics).getByText("380 ms")).toBeVisible();
    expect(within(metrics).getByText("slowest checkout-web 480 ms")).toBeVisible();
    expect(within(metrics).getByText("Fullest disk")).toBeVisible();
    expect(within(metrics).getByText("/ on web-01")).toBeVisible();
    const outlook = screen.getByRole("region", { name: "Outlook" });
    expect(within(outlook).getByText("17 days")).toBeVisible();
    expect(within(outlook).getByText("23 days")).toBeVisible();
    expect(within(outlook).getByText("Disk / on web-01 is full at this pace (81% now)")).toBeVisible();
  });

  it("says what it needs when there are no metrics", async () => {
    vi.mocked(api.home).mockResolvedValue({
      ...stormSnapshot,
      metrics: { ...stormSnapshot.metrics, latency: null },
      disks: [],
    });

    renderHome();

    expect(await screen.findByText("Not measured")).toBeVisible();
    expect(screen.getByText("Send HTTP server metrics")).toBeVisible();
    expect(screen.getByText("Disk outlooks come with host metrics", { exact: false })).toBeVisible();
  });

  it("says when no watched disk is filling", async () => {
    vi.mocked(api.home).mockResolvedValue(clearSnapshot);

    renderHome();

    expect(await screen.findByText("No disk fills up within 30 days on the watched host")).toBeVisible();
  });

  it("shows the coming hours with their expected load", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);

    renderHome();

    const strip = await screen.findByRole("list", { name: "Hours" });
    const hours = within(strip).getAllByRole("listitem");
    expect(hours).toHaveLength(13);
    expect(hours[6]).toHaveTextContent("Now5.8%");
    expect(hours[7]).toHaveTextContent("081.1×");
    expect(hours[12]).toHaveTextContent("13learning");
  });

  it("picks production when it exists and nothing was chosen", async () => {
    const all: HomeSnapshot = {
      ...clearSnapshot,
      environment: null,
      environments: [
        { name: "production", condition: "Clear", errorRatio: 0.001 },
        { name: "staging", condition: "Showers", errorRatio: 0.02 },
      ],
    };
    vi.mocked(api.home).mockImplementation((environment) =>
      Promise.resolve({ ...all, environment: environment ?? null }),
    );

    renderHome();

    await vi.waitFor(() => expect(api.home).toHaveBeenLastCalledWith("production", expect.anything()));
    expect(api.home).toHaveBeenCalledWith(null, expect.anything());
    expect(await screen.findByRole("button", { name: /production/, pressed: true })).toBeInTheDocument();
  });

  it("switches environment from the pills and remembers the choice", async () => {
    const snapshot: HomeSnapshot = {
      ...clearSnapshot,
      environments: [
        { name: "production", condition: "Clear", errorRatio: 0.001 },
        { name: "staging", condition: "Showers", errorRatio: 0.02 },
      ],
    };
    vi.mocked(api.home).mockImplementation((environment) => Promise.resolve({ ...snapshot, environment }));
    const user = userEvent.setup();

    renderHome();
    await user.click(await screen.findByRole("button", { name: /staging/ }));

    await vi.waitFor(() => expect(api.home).toHaveBeenLastCalledWith("staging", expect.anything()));
    expect(localStorage.getItem("veyra.home.environment")).toBe(JSON.stringify({ environment: "staging" }));
  });

  it("refreshes every 15 seconds and keeps the last picture when a refresh fails", async () => {
    // Chosen before, so the screen does not switch environments by itself and only the timer fetches again.
    localStorage.setItem("veyra.home.environment", JSON.stringify({ environment: "production" }));
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(api.home).mockResolvedValueOnce(stormSnapshot).mockRejectedValueOnce(new ApiError(503, "Unavailable"));

    renderHome();
    expect(await screen.findByRole("heading", { level: 1, name: "Storm over Services" })).toBeInTheDocument();
    const calls = vi.mocked(api.home).mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(15_000);
    });

    expect(vi.mocked(api.home).mock.calls.length).toBe(calls + 1);
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not refresh: Unavailable");
    expect(screen.getByRole("heading", { level: 1, name: "Storm over Services" })).toBeInTheDocument();
  });

  it("says no records, without a percent sign, when no logs arrived", async () => {
    vi.mocked(api.home).mockResolvedValue({
      ...clearSnapshot,
      blocks: [],
      condition: { kind: "Clear", over: null, errorRatio: null },
    });

    renderHome();

    expect(await screen.findByTestId("hero-ratio")).toHaveTextContent("No recordsin the last 5 minutes");
    expect(screen.getByText("No logs arrived in the last hour.")).toBeVisible();
  });

  it("explains a failure before the first picture", async () => {
    vi.mocked(api.home).mockRejectedValue(new ApiError(500, "Storage is not configured."));

    renderHome();

    expect(await screen.findByRole("alert")).toHaveTextContent("Storage is not configured.");
  });
});
