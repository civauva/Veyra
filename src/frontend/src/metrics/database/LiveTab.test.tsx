import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../api/client";
import { api } from "../../api/client";
import { LiveTab } from "./LiveTab";
import { liveQuietView, liveView } from "./testDatabase";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { databaseLive: vi.fn() } };
});

/** Renders the tab for the SQL Server of the fixtures. */
function renderTab(advanced: boolean, onAdvanced = vi.fn()) {
  return render(
    <MemoryRouter>
      <LiveTab
        engine="microsoft.sql_server"
        server="sql-prod-01"
        at={null}
        advanced={advanced}
        onNow={vi.fn()}
        onAdvanced={onAdvanced}
      />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(api.databaseLive).mockResolvedValue(liveView);
});

describe("LiveTab", () => {
  it("leads the Simple view with the worst finding and lists what runs", async () => {
    renderTab(false);

    expect(await screen.findByRole("heading", { level: 2, name: "One connection holds up another" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Open connection 73" })).toBeVisible();
    expect(screen.getByRole("link", { name: /logs/ })).toHaveAttribute(
      "href",
      "/logs?service=shop-admin&around=2026-11-10T11%3A00%3A08Z",
    );

    const running = screen.getByRole("region", { name: "Running now" });
    expect(within(running).getByText("Monthly sales by region")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Sessions" })).not.toBeInTheDocument();
  });

  it("opens a session panel from a running row", async () => {
    const user = userEvent.setup();
    renderTab(false);

    const running = await screen.findByRole("region", { name: "Running now" });
    await user.click(within(running).getByRole("button", { name: /Monthly sales by region/ }));

    expect(screen.getByRole("dialog")).toHaveTextContent("EXEC dbo.MonthlyReport @month");
  });

  it("offers the Advanced view from a finding", async () => {
    const user = userEvent.setup();
    const onAdvanced = vi.fn();
    renderTab(false, onAdvanced);

    await user.click(await screen.findByRole("button", { name: /See it in Advanced/ }));

    expect(onAdvanced).toHaveBeenCalledOnce();
  });

  it("shows the blocking tree and every session in the Advanced view", async () => {
    renderTab(true);

    expect(await screen.findByRole("heading", { level: 2, name: "Blocking now" })).toBeVisible();

    const sessions = screen.getByRole("region", { name: "Sessions" });
    expect(within(sessions).getAllByRole("row").length).toBeGreaterThan(3);
  });

  it("marks the open session's row in the blocking tree", async () => {
    const user = userEvent.setup();
    renderTab(true);

    const tree = await screen.findByRole("region", { name: "Blocking now" });
    await user.click(within(tree).getByRole("button", { name: /^session\s*117$/ }));

    expect(screen.getByRole("dialog")).toBeVisible();
    const open = tree.querySelector("[aria-current='true']");
    expect(open).toHaveTextContent("117");
    expect(
      within(tree)
        .getByRole("button", { name: /^session\s*73$/ })
        .closest("[aria-current]"),
    ).toBeNull();
  });

  it("says nobody waits when nothing blocks", async () => {
    vi.mocked(api.databaseLive).mockResolvedValue(liveQuietView);
    renderTab(false);

    expect(await screen.findByText("nobody waits")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Who waits for whom" })).not.toBeInTheDocument();
  });
});
