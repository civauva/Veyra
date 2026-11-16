import { expect, test, type Page } from "@playwright/test";

import { overviewWithDatabase } from "../src/metrics/testMetrics";
import { answerApi } from "./api";

/** Whether the page, or the screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const screen = document.querySelector(".metrics");

    return (
      document.documentElement.scrollWidth > window.innerWidth ||
      (screen !== null && screen.scrollWidth > screen.clientWidth)
    );
  });
}

/** Opens the SQL Server screen from its card on the Metrics overview. */
async function openServer(page: Page, view: "simple" | "advanced"): Promise<void> {
  await answerApi(page, { view });
  await page.route("**/api/v1/metrics/overview**", (route) => route.fulfill({ json: overviewWithDatabase }));
  await page.goto("/metrics");

  const card = page.getByRole("region", { name: "Databases" }).getByRole("article", { name: "sql-prod-01" });
  await expect(card).toContainText("1 job failed");
  expect(await scrollsSideways(page)).toBe(false);

  await card.getByRole("link").click();
  await expect(page.getByRole("heading", { level: 1, name: "sql-prod-01" })).toBeVisible();
}

/** Opens one tab of the server screen, and says the tab is the one shown. */
async function openTab(page: Page, label: string): Promise<void> {
  const tab = page.getByRole("navigation", { name: "Server views" }).getByRole("button", { name: label, exact: true });

  await tab.click();
  await expect(tab).toHaveAttribute("aria-current", "page");
}

/** A SQL Server on the Metrics overview and its tabs, in both views, at every size (VEY-34, ADR 0020). */
test.describe("Database servers", () => {
  test("opens the SQL Server from its card and fits every Simple tab", async ({ page }) => {
    await openServer(page, "simple");

    const tabs = page.getByRole("navigation", { name: "Server views" });
    await expect(tabs.getByRole("button", { name: "Waits" })).toBeHidden();

    await expect(page.getByRole("heading", { level: 2, name: "One connection holds up another" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Running now" })).toContainText("Monthly sales by region");
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Queries");
    await expect(page.getByRole("heading", { level: 2, name: "Queries worth a look" })).toBeVisible();
    await expect(page.getByText("A parameter has the wrong type")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Blocking");
    await expect(page.getByRole("heading", { level: 2, name: "Blocking" })).toBeVisible();
    await expect(page.getByText("Shop.Admin on admin-01 held a lock")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Databases");
    await expect(page.getByRole("region", { name: "Databases" })).toContainText("shop");
    await expect(page.getByRole("heading", { level: 2, name: "Needs doing" })).toBeVisible();
    await expect(page.getByText("shop has no full backup for 1 d 7 h")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Jobs");
    await expect(page.getByRole("heading", { level: 2, name: "SQL Agent jobs" })).toBeVisible();
    await expect(page.getByText("Nightly full backup - shop")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Events");
    await expect(page.getByRole("heading", { level: 2, name: "Events" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Server");
    await expect(page.getByRole("heading", { level: 2, name: "The server" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("adds the Waits tab and the DBA's detail in the Advanced view", async ({ page }) => {
    await openServer(page, "simple");

    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Advanced" }).click();
    await expect(page.getByRole("heading", { level: 2, name: "Blocking now" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Sessions" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Queries");
    await expect(page.getByRole("heading", { level: 2, name: "Statements" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Monthly sales by region" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Waits");
    await expect(page.getByRole("region", { name: "Waits" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Now" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Wait types" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Blocking");
    await expect(page.getByRole("heading", { level: 2, name: "Deadlocks" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Databases");
    await expect(page.getByRole("heading", { level: 2, name: "Each database" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Indexes and statistics" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Jobs");
    await expect(page.getByText("Index maintenance")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Events");
    await expect(page.getByRole("heading", { level: 2, name: "Events" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Server");
    await expect(page.getByRole("heading", { level: 2, name: "CPU, memory, and workload" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Availability" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "Configuration and security" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("opens a session from the Live tab in a side panel", async ({ page }) => {
    await openServer(page, "simple");

    await page
      .getByRole("region", { name: "Running now" })
      .getByRole("button", { name: /Monthly sales by region/ })
      .click();

    const panel = page.getByRole("dialog");
    await expect(panel).toContainText("Connection 203");
    await expect(panel).toContainText("EXEC dbo.MonthlyReport @month");
    expect(await scrollsSideways(page)).toBe(false);

    await page.keyboard.press("Escape");
    await expect(panel).toBeHidden();
  });

  test("leads from a statement on the Queries tab to its page", async ({ page }) => {
    await openServer(page, "advanced");
    await openTab(page, "Queries");

    await page.getByRole("link", { name: "Monthly sales by region" }).click();

    await expect(page).toHaveURL(/\/queries\/0x7A3C91D2E4F05B68/);
    await expect(page.getByRole("heading", { level: 1, name: "Monthly sales by region" })).toBeVisible();
    await expect(page.getByRole("heading", { level: 2, name: "How to fix it" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });
});
