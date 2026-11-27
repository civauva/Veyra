import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { AlertListItem } from "../api/types";
import { BlockStatus } from "./BlockStatus";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { alerts: vi.fn() } };
});

/** A caution on web-01: its root disk is filling. */
const diskAlert: AlertListItem = {
  id: "0193a2b4-0000-7000-8000-000000000101",
  rule: "disk-space",
  environment: "production",
  block: { kind: "host", name: "web-01", part: "/", id: "host:production/web-01" },
  severity: "Caution",
  worst: "Caution",
  value: 0.86,
  peak: 0.86,
  peakAt: null,
  threshold: 0.85,
  windowSeconds: 300,
  openedAt: "2026-11-10T03:00:00Z",
  updatedAt: "2026-11-10T11:00:00Z",
  resolvedAt: null,
  area: null,
};

/** A second alert on web-01, opened after the first was dismissed. */
const fillingAlert: AlertListItem = { ...diskAlert, id: "0193a2b4-0000-7000-8000-000000000102", rule: "disk-filling" };

/** Renders the banner of web-01, as its host screen does. */
function renderBanner() {
  return render(
    <MemoryRouter>
      <BlockStatus kind="host" name="web-01" />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  localStorage.clear();
  vi.mocked(api.alerts).mockResolvedValue({ items: [diskAlert], next: null });
});

describe("BlockStatus", () => {
  it("names the worst open alert, and closes when dismissed", async () => {
    const user = userEvent.setup();
    renderBanner();

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("web-01");

    await user.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("stays closed for the alerts dismissed, and comes back when another opens", async () => {
    const user = userEvent.setup();
    const first = renderBanner();
    await user.click(await screen.findByRole("button", { name: "Dismiss" }));
    first.unmount();

    const again = renderBanner();
    await vi.waitFor(() => expect(api.alerts).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    again.unmount();

    vi.mocked(api.alerts).mockResolvedValue({ items: [diskAlert, fillingAlert], next: null });
    renderBanner();

    expect(await screen.findByRole("status")).toHaveTextContent("1 more alert is open.");
  });

  it("comes back when a dismissed alert changes severity", async () => {
    const user = userEvent.setup();
    const first = renderBanner();
    await user.click(await screen.findByRole("button", { name: "Dismiss" }));
    first.unmount();

    vi.mocked(api.alerts).mockResolvedValue({ items: [{ ...diskAlert, severity: "Warning" }], next: null });
    renderBanner();

    expect(await screen.findByRole("status")).toBeVisible();
  });

  it("asks the server for exactly this block's alerts, the worst first, and names the first it sends", async () => {
    const warning = { ...fillingAlert, severity: "Warning" as const, worst: "Warning" as const };
    vi.mocked(api.alerts).mockResolvedValue({ items: [warning, diskAlert], next: null });
    renderBanner();

    expect(await screen.findByRole("status")).toHaveClass("warning");
    expect(api.alerts).toHaveBeenCalledWith(
      expect.objectContaining({ kind: "host", block: "web-01", order: "worst", text: "" }),
      null,
      expect.anything(),
    );
  });

  it("offers no dismiss when all is fine", async () => {
    vi.mocked(api.alerts).mockResolvedValue({ items: [], next: null });
    renderBanner();

    expect(await screen.findByRole("status")).toHaveTextContent("web-01 is fine.");
    expect(screen.queryByRole("button", { name: "Dismiss" })).not.toBeInTheDocument();
  });

  it("uses the alerts its screen already read instead of reading them again", () => {
    render(
      <MemoryRouter>
        <BlockStatus kind="host" name="web-01" alerts={[diskAlert]} />
      </MemoryRouter>,
    );

    expect(screen.getByRole("status")).toBeVisible();
    expect(api.alerts).not.toHaveBeenCalled();
  });
});
