import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../api/client";
import { api } from "../../api/client";
import { QueriesTab } from "./QueriesTab";
import { monthlyReportHash, queriesView } from "./testDatabase";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { databaseQueries: vi.fn() } };
});

/** Renders the tab for the SQL Server of the fixtures. */
function renderTab(advanced: boolean) {
  return render(
    <MemoryRouter>
      <QueriesTab engine="microsoft.sql_server" server="sql-prod-01" range="6h" advanced={advanced} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(api.databaseQueries).mockResolvedValue(queriesView);
});

describe("QueriesTab", () => {
  it("lists only the statements with a finding in the Simple view", async () => {
    renderTab(false);

    const worth = await screen.findByRole("region", { name: "Queries worth a look" });
    expect(within(worth).getByText("A parameter has the wrong type")).toBeVisible();
    expect(within(worth).queryByText(/Update the status of an order/)).not.toBeInTheDocument();
    expect(within(worth).getByRole("link", { name: /How to fix it/ })).toHaveAttribute(
      "href",
      `/metrics/databases/microsoft.sql_server/sql-prod-01/queries/${monthlyReportHash}?range=6h`,
    );
  });

  it("offers the statement that turns Query Store on", async () => {
    renderTab(false);

    const store = await screen.findByRole("region", { name: "Query Store is off" });
    expect(within(store).getByText("reporting")).toBeVisible();
    expect(within(store).getByRole("button", { name: "Copy the statement" })).toBeVisible();
  });

  it("ranks every statement in the Advanced view, by the measure chosen", async () => {
    const user = userEvent.setup();
    renderTab(true);

    const ranking = await screen.findByRole("region", { name: "Statements" });
    const names = () =>
      within(ranking)
        .getAllByRole("rowheader")
        .map((cell) => cell.textContent);
    expect(names()[0]).toContain("Monthly sales by region");

    await user.click(within(ranking).getByRole("button", { name: "Runs" }));

    expect(names()[0]).toContain("Update the status of an order");
    expect(within(ranking).getByText(/Not ranked: .* 6 statements/)).toBeVisible();
  });
});
