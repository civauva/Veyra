import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { stormSnapshot } from "../home/testSnapshots";
import { SessionProvider } from "../session/SessionProvider";
import { openWarning, resolvedCaution } from "./testAlerts";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      alert: vi.fn(),
      searchLogs: vi.fn(),
      summarizeLogs: vi.fn(),
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
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [], nextCursor: null });
  vi.mocked(api.summarizeLogs).mockResolvedValue({ bucketSeconds: 60, buckets: [], services: [] });
  localStorage.clear();
});

describe("AlertScreen", () => {
  it("tells what the alert says, when it crossed each threshold, and paints the storm", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    const { container } = renderAt(`/alerts/${openWarning.id}`);

    expect(
      await screen.findByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Open · warning · 36 min")).toBeVisible();
    expect(screen.getByText(/went above 1% at 06:58 and above 5% at 07:01/)).toBeVisible();
    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-storm"));
    expect(vi.mocked(api.alert)).toHaveBeenCalledWith(openWarning.id, expect.any(AbortSignal));
  });

  it("shows the five facts with the channels that were told", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    const peak = (await screen.findByText("Peak")).closest("div");
    expect(peak).toHaveTextContent("24.1%at 07:12");
    const notified = screen.getByText("Notified").closest("div");
    expect(notified).toHaveTextContent("3#ops-alerts, On-call, Incident bot");
  });

  it("lists every change with its messages, and why a failing one has not gone through", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    const timeline = await screen.findByRole("region", { name: "Timeline" });
    const rows = within(timeline).getAllByRole("listitem");
    expect(rows.map((row) => row.textContent)).toEqual([
      "06:58Opened as caution1.8% errors",
      "06:58Webhook #ops-alertsFirst trydelivered · 204",
      "07:01Rose to warning6.2% errors",
      "07:01Webhook #ops-alertsFirst trydelivered · 204",
      "07:01Email On-callFirst trysent",
      "07:04Webhook Incident botTry 3 failed: Timed out after 10 s. Next try 07:12.retrying",
    ]);
  });

  it("draws the chart with a summary for screen readers", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    expect(
      await screen.findByRole("img", {
        name: "Error rate of payments-gateway from 06:28 to 07:34 UTC, peak 24.1% at 07:12, warning from 5%, caution from 1%.",
      }),
    ).toBeInTheDocument();
  });

  it("links the most frequent errors into the Log Explorer", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);
    const user = userEvent.setup();

    renderAt(`/alerts/${openWarning.id}`);

    const top = await screen.findByRole("region", { name: "Most frequent errors since 06:53" });
    expect(within(top).getAllByRole("listitem")).toHaveLength(5);
    await user.click(
      within(top).getByRole("link", { name: "Logs with “Circuit open for card-processor, request rejected”" }),
    );

    expect(await screen.findByRole("heading", { name: "Logs" })).toBeInTheDocument();
    const params = vi.mocked(api.searchLogs).mock.calls.at(-1)?.[0];
    expect(params?.get("text")).toBe("Circuit open for card-processor, request rejected");
    expect(params?.getAll("service")).toEqual(["payments-gateway"]);
    expect(params?.get("severity")).toBe("Error");
  });

  it("paints clear skies for a resolved alert and says how long it lasted", async () => {
    vi.mocked(api.alert).mockResolvedValue(resolvedCaution);

    const { container } = renderAt(`/alerts/${resolvedCaution.id}`);

    expect(await screen.findByRole("heading", { level: 1, name: "orders-api is back to 0.4% errors" })).toBeVisible();
    expect(screen.getByText("Resolved · was caution · lasted 14 min")).toBeVisible();
    expect(screen.getByText("Lasted").closest("div")).toHaveTextContent("14 min03:10–03:24 UTC");
    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-clear"));
    expect(screen.getByText(/No channel was told about this alert/)).toBeVisible();
  });

  it("explains an unknown or purged alert", async () => {
    vi.mocked(api.alert).mockRejectedValue(new ApiError(404, "Not Found"));

    renderAt("/alerts/0193a2b4-0000-7000-8000-00000000ffff");

    expect(await screen.findByText(/Resolved alerts are kept for 90 days/)).toBeVisible();
    expect(screen.getByRole("link", { name: "← Alerts" })).toHaveAttribute("href", "/alerts");
  });

  it("is reached from Needs you on the home", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);
    vi.mocked(api.alert).mockResolvedValue(openWarning);
    const user = userEvent.setup();

    renderAt("/");

    const pane = await screen.findByRole("region", { name: "Needs you" });
    await user.click(within(pane).getByRole("link", { name: "Details" }));

    expect(await screen.findByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" })).toBeVisible();
  });
});
