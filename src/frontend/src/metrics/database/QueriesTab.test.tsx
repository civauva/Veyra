import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../api/client";
import { api } from "../../api/client";
import type { MetricRange } from "../../api/types";
import { QueriesTab } from "./QueriesTab";
import { monthlyReportHash, queriesSimpleView, queriesView } from "./testDatabase";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { databaseQueries: vi.fn() } };
});

/** Renders the tab for the SQL Server of the fixtures, over the last 6 hours unless a range is given. */
function renderTab(advanced: boolean, range: MetricRange = "6h") {
  return render(
    <MemoryRouter>
      <QueriesTab engine="microsoft.sql_server" server="sql-prod-01" range={range} advanced={advanced} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  // The server keeps the Simple view to the statements with a finding (VEY-67).
  vi.mocked(api.databaseQueries).mockImplementation((_engine, _server, _range, _system, _sort, simple) =>
    Promise.resolve(simple ? queriesSimpleView : queriesView),
  );
});

describe("QueriesTab", () => {
  it("asks the server for the statements with a finding in the Simple view and lists its answer", async () => {
    renderTab(false);

    const worth = await screen.findByRole("region", { name: "Queries worth a look" });
    expect(within(worth).getByText("A parameter has the wrong type")).toBeVisible();
    expect(within(worth).queryByText(/Update the status of an order/)).not.toBeInTheDocument();
    expect(within(worth).getByRole("link", { name: /How to fix it/ })).toHaveAttribute(
      "href",
      `/metrics/databases/microsoft.sql_server/sql-prod-01/queries/${monthlyReportHash}?range=6h`,
    );
  });

  it("tells a window of the person's own in words and carries it to the statement", async () => {
    const from = "2026-11-25T09:00:00.000Z";
    const to = "2026-11-26T09:00:00.000Z";
    renderTab(false, `${from}~${to}`);

    const worth = await screen.findByRole("region", { name: "Queries worth a look" });
    expect(within(worth).getByText("from 25 Nov 09:00 to 26 Nov 09:00")).toBeVisible();
    const window = new URLSearchParams({ from, to }).toString();
    expect(within(worth).getByRole("link", { name: /How to fix it/ })).toHaveAttribute(
      "href",
      `/metrics/databases/microsoft.sql_server/sql-prod-01/queries/${monthlyReportHash}?${window}`,
    );
    expect(api.databaseQueries).toHaveBeenCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      `${from}~${to}`,
      false,
      "totalTime",
      true,
      expect.anything(),
    );
  });

  it("names the busiest statement the server gives when none shows a problem", async () => {
    vi.mocked(api.databaseQueries).mockResolvedValue({ ...queriesSimpleView, statements: [] });
    renderTab(false);

    expect(await screen.findByRole("link", { name: queriesView.busiest?.name ?? "" })).toBeVisible();
  });

  it("offers the statement that turns Query Store on", async () => {
    renderTab(false);

    const store = await screen.findByRole("region", { name: "Query Store is off" });
    expect(within(store).getByText("reporting")).toBeVisible();
    expect(within(store).getByRole("button", { name: "Copy the statement" })).toBeVisible();
  });

  it("asks the server to rank by the measure chosen, and lists the statements in its order", async () => {
    // The server ranks before it keeps the top 100, so a statement outside the slowest still shows (VEY-66).
    vi.mocked(api.databaseQueries).mockImplementation((_engine, _server, _range, _system, sort) =>
      Promise.resolve(
        sort === "runs" ? { ...queriesView, statements: [...queriesView.statements].reverse() } : queriesView,
      ),
    );
    const user = userEvent.setup();
    renderTab(true);

    const ranking = await screen.findByRole("region", { name: "Statements" });
    const names = () =>
      within(ranking)
        .getAllByRole("rowheader")
        .map((cell) => cell.textContent);
    expect(names()[0]).toContain("Monthly sales by region");

    await user.click(within(ranking).getByRole("button", { name: "Runs" }));

    expect(api.databaseQueries).toHaveBeenLastCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      "6h",
      false,
      "runs",
      false,
      expect.any(AbortSignal),
    );
    await waitFor(() => expect(names()[0]).toContain("Update the status of an order"));
    expect(within(ranking).getByRole("button", { name: "Runs" })).toHaveAttribute("aria-pressed", "true");
    expect(within(ranking).getByText(/Not ranked: .* 6 statements/)).toBeVisible();
  });

  it("asks for the busiest statements in the Simple view, whatever the Advanced view ranked by", async () => {
    renderTab(false);

    await screen.findByRole("region", { name: "Queries worth a look" });

    expect(api.databaseQueries).toHaveBeenLastCalledWith(
      "microsoft.sql_server",
      "sql-prod-01",
      "6h",
      false,
      "totalTime",
      true,
      expect.any(AbortSignal),
    );
  });
});
