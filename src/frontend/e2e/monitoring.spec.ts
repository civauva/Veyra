import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page is wider than the window, the usual sign of a layout that breaks on small screens. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
}

test.describe("Database monitoring and agents", () => {
  test("shows each SQL Server with its state in words at every size", async ({ page }) => {
    await answerApi(page, { view: "simple" });
    await page.goto("/settings?section=Databases");

    await expect(page.getByRole("heading", { name: "Database monitoring" })).toBeVisible();
    const refused = page.getByRole("article", { name: "sql-report-02" });
    await expect(refused).toContainText("Login failed");
    await expect(refused).toContainText("SQL Server refused the login veyra_monitor");
    await expect(page.getByRole("status", { name: "Agent" })).toContainText("Read by the built-in agent · Online");
    await expect(page.getByRole("article", { name: "sql-prod-01" })).toContainText("Receiving");
    await expect(page.getByRole("article", { name: "sql-branch-03" })).toContainText("Waiting for first data");
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("adds a SQL Server from the panel, keyboard and all, and follows the check", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings?section=Databases");

    await page.getByRole("button", { name: "+ Add SQL Server" }).click();
    const panel = page.getByRole("dialog", { name: "Add a SQL Server" });
    await expect(panel.getByRole("textbox", { name: /Name in Veyra/ })).toBeFocused();
    await page.keyboard.type("sql-branch-04");
    await page.keyboard.press("Tab");
    await page.keyboard.type("10.20.0.15");
    await expect(panel.getByLabel("Login script")).toContainText("CREATE LOGIN [veyra_monitor]");
    expect(await scrollsSideways(page)).toBe(false);

    await panel.getByRole("button", { name: "Save and start watching" }).click();

    await expect(panel.getByRole("status")).toContainText("Waiting for first data");
    await expect(panel.getByText("Reached 10.20.0.15:1433")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(panel).toBeHidden();
  });

  test("shows a PostgreSQL server and adds another, keyboard and all (VEY-33)", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings?section=Databases");

    const main = page.getByRole("article", { name: "pg-main" });
    await expect(main).toContainText("PostgreSQL · pg-main.corp.local:5432");
    await expect(main.getByRole("link", { name: "Open" })).toHaveAttribute(
      "href",
      "/metrics/databases/postgresql/pg-main",
    );
    expect(await scrollsSideways(page)).toBe(false);

    await page.getByRole("button", { name: "+ Add PostgreSQL" }).click();
    const panel = page.getByRole("dialog", { name: "Add a PostgreSQL server" });
    await expect(panel.getByRole("textbox", { name: /Name in Veyra/ })).toBeFocused();
    await page.keyboard.type("pg-branch");
    await page.keyboard.press("Tab");
    await page.keyboard.type("10.20.0.16");
    await expect(panel.getByLabel("Role script")).toContainText('GRANT pg_monitor TO "veyra_monitor";');
    expect(await scrollsSideways(page)).toBe(false);

    await panel.getByRole("button", { name: "Save and start watching" }).click();

    await expect(panel.getByRole("status")).toContainText("Waiting for first data");
    await expect(panel.getByText("Reached 10.20.0.16:5432")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(panel).toBeHidden();
  });

  test("shows the built-in agent online", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings?section=Agents");

    const agent = page.getByRole("article", { name: "Built-in agent" });
    await expect(agent).toContainText("Online");
    await expect(agent).toContainText("Beside Veyra on veyra-agent");
    const guide = page.getByRole("group", { name: "How the built-in agent runs" });
    await guide.getByText("How the built-in agent runs").click();
    await expect(guide.getByLabel("Start it", { exact: true })).toHaveText(
      "docker compose -f deploy/docker-compose.yml up -d --no-deps agent",
    );
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("adds an agent for another network and shows the line that installs it", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings?section=Agents");

    await page.getByRole("button", { name: "Add an agent" }).click();
    await page.getByRole("textbox", { name: "Name" }).fill("Company network");
    await page.keyboard.press("Enter");

    const box = page.getByRole("region", { name: "Install Company network" });
    await expect(box).toContainText("K7QM-4TWX-9PRA");
    await expect(box.getByLabel("Install command", { exact: true })).toContainText(
      "Install-VeyraAgent -Code K7QM-4TWX-9PRA",
    );
    await box.getByRole("tab", { name: "Linux" }).click();
    await expect(box.getByLabel("Install command", { exact: true })).toContainText(
      "sudo sh -s -- --code K7QM-4TWX-9PRA",
    );
    await expect(box.getByRole("status")).toContainText("Waiting for Company network to connect");
    expect(await scrollsSideways(page)).toBe(false);
  });
});
