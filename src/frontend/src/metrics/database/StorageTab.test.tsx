import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../api/client";
import { api, ApiError } from "../../api/client";
import { StorageTab } from "./StorageTab";
import { asServer, storageView } from "./testDatabase";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { databaseStorage: vi.fn() } };
});

/** Renders the tab for the SQL Server of the fixtures. */
function renderTab(advanced: boolean) {
  return render(<StorageTab engine="microsoft.sql_server" server="sql-prod-01" advanced={advanced} />);
}

beforeEach(() => {
  // The server leaves its own databases out unless System is on (VEY-66).
  vi.mocked(api.databaseStorage).mockImplementation((_engine, _server, system) => {
    const databases = asServer(storageView.databases, system, (database) => database.name);

    return Promise.resolve({ ...storageView, databases: databases.rows, hiddenDatabases: databases.hidden });
  });
});

describe("StorageTab", () => {
  it("says what needs doing and shows the volumes in the Simple view", async () => {
    renderTab(false);

    const needs = await screen.findByRole("region", { name: "Needs doing" });
    expect(within(needs).getByText("shop has no full backup for 1 d 7 h")).toBeVisible();
    expect(within(needs).getByRole("button", { name: "Copy the statement" })).toBeVisible();
    expect(within(screen.getByRole("region", { name: "Volumes" })).getByText("85%")).toBeVisible();
    expect(screen.queryByRole("region", { name: "Each database" })).not.toBeInTheDocument();
  });

  it("adds each database's settings, files, and backups in the Advanced view", async () => {
    renderTab(true);

    const each = await screen.findByRole("region", { name: "Each database" });
    // shop and reporting; tempdb is the server's own and waits for System (VEY-64).
    expect(within(each).getAllByText(/last good CHECKDB 2 d ago/)).toHaveLength(2);
    expect(within(each).getByRole("note")).toHaveTextContent("1 system database hidden.");
    expect(within(each).getAllByText("shop_data")).toHaveLength(1);
  });

  it("says when the agent has not sent a storage read yet", async () => {
    vi.mocked(api.databaseStorage).mockRejectedValue(new ApiError(404, "Not found"));
    renderTab(false);

    expect(await screen.findByText(/has not sent a storage read yet/)).toBeVisible();
  });
});
