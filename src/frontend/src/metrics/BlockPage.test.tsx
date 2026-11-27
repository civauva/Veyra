import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it } from "vitest";

import type { AlertListItem } from "../api/types";
import { BlockPage } from "./BlockPage";
import { BlockStatus } from "./BlockStatus";
import { sqlProdBackupAlert, sqlProdHeadBlockerAlert } from "./database/testDatabase";

/** The block name of the test server. */
const server = "microsoft.sql_server sql-prod-01";

/**
 * Renders a server screen's content as the screen does: the banner in the column, the rail beside it.
 *
 * @param alerts - The server's open alerts.
 */
function renderPage(alerts: AlertListItem[] | null) {
  return render(
    <MemoryRouter>
      <BlockPage kind="database" name={server} alerts={alerts}>
        <BlockStatus kind="database" name={server} alerts={alerts} />
      </BlockPage>
    </MemoryRouter>,
  );
}

/** The rail, found by its name. */
function rail() {
  return screen.getByRole("complementary", { name: "Open alerts" });
}

beforeEach(() => {
  localStorage.clear();
});

describe("BlockPage", () => {
  it("lists the open alerts in the order the server sends them, each leading to its details", () => {
    // The server sends them the worst first (VEY-68); the rail keeps its order.
    renderPage([sqlProdHeadBlockerAlert, sqlProdBackupAlert]);

    expect(within(rail()).getByRole("heading", { name: /Open alerts/ })).toHaveTextContent("2");

    const links = within(within(rail()).getByRole("list")).getAllByRole("link");
    expect(links).toHaveLength(2);
    expect(links[0]).toHaveAttribute("href", `/alerts/${sqlProdHeadBlockerAlert.id}`);
    expect(links[1]).toHaveAttribute("href", `/alerts/${sqlProdBackupAlert.id}`);

    // The screen already names the server, so the rows leave it out, and say the area the alert belongs to.
    expect(links[1]).not.toHaveTextContent("sql-prod-01:");
    expect(links[1]).toHaveTextContent("shop");
    expect(links[1]).toHaveTextContent(/Databases · open /);
    expect(links[0]).toHaveTextContent(/Blocking · open /);
  });

  it("shows six alerts and leads to the rest on the Alerts screen", () => {
    const many = Array.from({ length: 8 }, (_, index) => ({ ...sqlProdBackupAlert, id: `alert-${index}` }));
    renderPage(many);

    expect(within(within(rail()).getByRole("list")).getAllByRole("link")).toHaveLength(6);
    expect(within(rail()).getByRole("link", { name: "2 more" })).toHaveAttribute(
      "href",
      `/alerts?state=open&q=${encodeURIComponent(server)}`,
    );
    expect(within(rail()).getByRole("link", { name: "All alerts" })).toHaveAttribute(
      "href",
      `/alerts?state=open&q=${encodeURIComponent(server)}`,
    );
  });

  it("is not drawn without open alerts, or while they are unknown", () => {
    const { rerender } = renderPage([]);
    expect(screen.queryByRole("complementary")).not.toBeInTheDocument();

    rerender(
      <MemoryRouter>
        <BlockPage kind="database" name={server} alerts={null}>
          <p>content</p>
        </BlockPage>
      </MemoryRouter>,
    );
    expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
    expect(screen.getByText("content")).toBeInTheDocument();
  });

  it("hides with the banner, until an alert opens that was not open then", async () => {
    const user = userEvent.setup();
    const { rerender } = renderPage([sqlProdBackupAlert]);
    expect(screen.getByRole("status")).toBeInTheDocument();

    await user.click(within(rail()).getByRole("button", { name: "Hide the open alerts" }));

    expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    rerender(
      <MemoryRouter>
        <BlockPage kind="database" name={server} alerts={[sqlProdBackupAlert, sqlProdHeadBlockerAlert]}>
          <BlockStatus kind="database" name={server} alerts={[sqlProdBackupAlert, sqlProdHeadBlockerAlert]} />
        </BlockPage>
      </MemoryRouter>,
    );

    expect(rail()).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("hides when the banner is dismissed", async () => {
    const user = userEvent.setup();
    renderPage([sqlProdBackupAlert]);

    await user.click(screen.getByRole("button", { name: "Dismiss" }));

    expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
  });
});
