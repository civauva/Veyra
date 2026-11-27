import { expect, test, type Page } from "@playwright/test";

import {
  permissionsMissingJobSteps,
  sqlProdBackupAlert,
  sqlProdHeadBlockerAlert,
} from "../src/metrics/database/testDatabase";
import { overviewWithDatabase, overviewWithPostgres, postgresMetrics } from "../src/metrics/testMetrics";
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
  await answerApi(page, { look: "forecast", view });
  await page.route("**/api/v1/metrics/overview**", (route) => route.fulfill({ json: overviewWithDatabase }));
  await page.goto("/metrics");

  const card = page.getByRole("region", { name: "Database servers" }).getByRole("article", { name: "sql-prod-01" });
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

/**
 * A SQL Server on the Metrics overview and its tabs, in both views, at every size (VEY-34, ADR 0020), and a
 * PostgreSQL server with its own tabs (VEY-33).
 */
test.describe("Database servers", () => {
  test("opens the SQL Server from its card and fits every Simple tab", async ({ page }) => {
    await openServer(page, "simple");

    const tabs = page.getByRole("navigation", { name: "Server views" });
    await expect(tabs.getByRole("button", { name: "Waits" })).toBeHidden();
    await expect(tabs.getByRole("button", { name: "Overview" })).toHaveAttribute("aria-current", "page");
    await expect(page.getByRole("list", { name: "Areas" }).getByRole("listitem")).toHaveCount(7);
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Live");
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
    await expect(page.getByRole("list", { name: "Areas" })).toContainText("Waits");
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Live");
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
    await openTab(page, "Live");

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

  test("opens the PostgreSQL server from its card and fits both tabs in both views", async ({ page }) => {
    await answerApi(page, { look: "forecast", view: "simple" });
    await page.route("**/api/v1/metrics/overview**", (route) => route.fulfill({ json: overviewWithPostgres }));
    await page.route("**/api/v1/metrics/databases/postgresql/pg-main**", (route) =>
      route.fulfill({ json: postgresMetrics }),
    );
    await page.goto("/metrics");

    const card = page.getByRole("region", { name: "Database servers" }).getByRole("article", { name: "pg-main" });
    await expect(card).toContainText("Wraparound 52%");
    expect(await scrollsSideways(page)).toBe(false);

    await card.getByRole("link").focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { level: 1, name: "pg-main" })).toBeVisible();

    const tabs = page.getByRole("navigation", { name: "Server views" });
    await expect(tabs.getByRole("button")).toHaveText(["Overview", "Databases"]);
    await expect(page.getByRole("region", { name: "Now" })).toContainText("86 of 100");
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Databases");
    await expect(page.getByRole("meter", { name: "Transaction ID age of shop" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Dead rows" })).toContainText("public.carts");
    expect(await scrollsSideways(page)).toBe(false);

    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Advanced" }).click();
    // Live rows show only in the Advanced view: 1.2M in public.carts.
    await expect(page.getByRole("region", { name: "Dead rows" })).toContainText("1.2M");
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Overview");
    await expect(page.getByRole("region", { name: "Connections" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Transactions" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("leads from the overview to an area, closes the banner, and moves to another server", async ({ page }) => {
    await answerApi(page, { look: "forecast", view: "simple" });
    await page.route("**/api/v1/metrics/overview**", (route) => route.fulfill({ json: overviewWithPostgres }));
    await page.route(
      (url) => url.pathname === "/api/v1/alerts" && url.searchParams.toString().includes("sql-prod-01"),
      // The server sends them the worst first (VEY-68).
      (route) => route.fulfill({ json: { items: [sqlProdHeadBlockerAlert, sqlProdBackupAlert], next: null } }),
    );
    // The server ranks the areas from those alerts, the worst first (VEY-68).
    await page.route(
      (url) => url.pathname === "/api/v1/alerts/areas",
      (route) => {
        const asked = new URL(route.request().url()).searchParams.getAll("area");

        return route.fulfill({
          json: [
            { area: "blocking", health: "Warning", alerts: [sqlProdHeadBlockerAlert] },
            { area: "databases", health: "Caution", alerts: [sqlProdBackupAlert] },
            ...asked
              .filter((area) => area !== "blocking" && area !== "databases")
              .map((area) => ({ area, health: "Ok", alerts: [] })),
          ],
        });
      },
    );
    await page.route("**/api/v1/metrics/databases/postgresql/pg-main**", (route) =>
      route.fulfill({ json: postgresMetrics }),
    );
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01");

    // The worst area comes first, as a storm.
    const areas = page.getByRole("list", { name: "Areas" });
    await expect(areas.getByRole("listitem").first()).toContainText("Blocking");
    await expect(areas.getByRole("button", { name: /^Blocking, storm: / })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    // The banner closes, and stays closed when the screen opens again.
    const banner = page.getByRole("status").filter({ hasText: "Alert details" });
    await expect(banner).toBeVisible();
    await banner.getByRole("button", { name: "Dismiss" }).click();
    await expect(banner).toBeHidden();
    await page.reload();
    await expect(areas).toBeVisible();
    await expect(banner).toBeHidden();

    // A card opens its tab.
    await areas.getByRole("button", { name: /^Databases, showers: / }).click();
    const tabs = page.getByRole("navigation", { name: "Server views" });
    await expect(tabs.getByRole("button", { name: "Databases" })).toHaveAttribute("aria-current", "page");

    // The switcher in the bar moves to the PostgreSQL server on the same tab.
    const trail = page.getByRole("navigation", { name: "Where you are" });
    await trail.getByRole("button", { name: /^sql-prod-01/ }).click();
    const menu = page.getByRole("menu", { name: "Database servers" });
    await expect(menu.getByRole("menuitem")).toHaveCount(2);
    expect(await scrollsSideways(page)).toBe(false);
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("Enter");

    await expect(page.getByRole("heading", { level: 1, name: "pg-main" })).toBeVisible();
    await expect(tabs.getByRole("button", { name: "Databases" })).toHaveAttribute("aria-current", "page");
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("says on the Jobs tab what the login lacks, instead of saying all is well", async ({ page }) => {
    await answerApi(page, { look: "forecast", view: "simple" });
    await page.route("**/api/v1/metrics/databases/*/*/permissions", (route) =>
      route.fulfill({ json: permissionsMissingJobSteps }),
    );
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=jobs");

    const notice = page.getByRole("list", { name: "What Veyra cannot read" });
    await expect(notice).toContainText("Veyra cannot read the steps of each job run on sql-prod-01");
    await expect(notice).toContainText("GRANT SELECT ON dbo.sysjobsteps TO [veyra_monitor];");
    expect(await scrollsSideways(page)).toBe(false);

    await openTab(page, "Live");
    await expect(notice).toBeHidden();
  });

  test("lists the open alerts beside the screen on a wide window, and in the banner below 1800 px", async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "Only a wide window has room for the rail.");
    await page.setViewportSize({ width: 2000, height: 600 });
    await answerApi(page, { look: "forecast", view: "advanced" });
    await page.route(
      (url) => url.pathname === "/api/v1/alerts" && url.searchParams.toString().includes("sql-prod-01"),
      // The server sends them the worst first (VEY-68).
      (route) => route.fulfill({ json: { items: [sqlProdHeadBlockerAlert, sqlProdBackupAlert], next: null } }),
    );
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=waits");
    await expect(page.getByRole("heading", { level: 1, name: "sql-prod-01" })).toBeVisible();

    // The rail lists the alerts, the worst first, and the banner gives way to it.
    const rail = page.getByRole("complementary", { name: "Open alerts" });
    const rows = rail.getByRole("list").getByRole("link");
    await expect(rows).toHaveCount(2);
    await expect(rows.first()).toContainText("Blocking · open");
    const banner = page.getByRole("status").filter({ hasText: "Alert details" });
    await expect(banner).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);

    // It sits to the right of the content and stays in view while the screen scrolls.
    const tiles = await page.getByRole("navigation", { name: "Server views" }).boundingBox();
    const before = await rail.boundingBox();
    expect(before?.x ?? 0).toBeGreaterThan((tiles?.x ?? 0) + (tiles?.width ?? 0));
    const scrolled = await page.locator(".metrics").evaluate((screen) => {
      screen.scrollTo(0, screen.scrollHeight);

      return screen.scrollTop;
    });
    expect(scrolled).toBeGreaterThan(200);
    await expect.poll(async () => (await rail.boundingBox())?.y ?? -1).toBeGreaterThanOrEqual(0);

    // A row opens the alert's details.
    await rows.first().click();
    await expect(page).toHaveURL(new RegExp(`/alerts/${sqlProdHeadBlockerAlert.id}$`));

    // Below 1800 px the banner tells the same alerts, and the rail is not shown.
    await page.goBack();
    await page.setViewportSize({ width: 1440, height: 900 });
    await expect(banner).toBeVisible();
    await expect(rail).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("asks the server for exactly the server's alerts, the worst first, and for its areas", async ({ page }) => {
    // The server keeps a block's alerts and ranks its areas (VEY-68), so the block must reach it.
    await answerApi(page, { look: "forecast" });
    const alerts = page.waitForRequest((request) => {
      const url = new URL(request.url());

      return (
        url.pathname === "/api/v1/alerts" &&
        url.searchParams.get("kind") === "database" &&
        url.searchParams.get("block") === "microsoft.sql_server sql-prod-01" &&
        url.searchParams.get("order") === "worst" &&
        !url.searchParams.has("q")
      );
    });
    const areas = page.waitForRequest((request) => {
      const url = new URL(request.url());

      return (
        url.pathname === "/api/v1/alerts/areas" &&
        url.searchParams.get("block") === "microsoft.sql_server sql-prod-01" &&
        url.searchParams.getAll("area").includes("blocking")
      );
    });

    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01");
    await alerts;
    await areas;
    await expect(page.getByRole("list", { name: "Areas" })).toBeVisible();
  });

  test("asks the server for the Simple view of the jobs and the queries", async ({ page }) => {
    // The server keeps the Simple view to what it shows (VEY-67), so the choice must reach it.
    await answerApi(page, { look: "forecast", view: "simple" });
    const asked = (path: string) =>
      page.waitForRequest((request) => {
        const url = new URL(request.url());

        return url.pathname.endsWith(path) && url.searchParams.get("simple") === "true";
      });

    const jobs = asked("/jobs");
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=jobs");
    await jobs;
    await expect(page.getByText("Nightly full backup - shop")).toBeVisible();
    await expect(page.getByText("Index maintenance")).toHaveCount(0);

    const queries = asked("/queries");
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=queries");
    await queries;
    await expect(page.getByRole("region", { name: "Queries worth a look" })).toBeVisible();
  });

  test("asks the server for every choice the screen offers, and never filters what comes back", async ({ page }) => {
    // The server filters and ranks before it keeps its top rows, so each choice must reach it (VEY-66).
    await answerApi(page, { look: "forecast", view: "advanced" });
    const asked = (path: string, name: string, value: string) =>
      page.waitForRequest((request) => {
        const url = new URL(request.url());

        return url.pathname.endsWith(path) && url.searchParams.get(name) === value;
      });

    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=queries");
    const ranking = page.getByRole("region", { name: "Statements" });
    await expect(ranking).toBeVisible();

    const reads = asked("/queries", "sort", "reads");
    await ranking.getByRole("button", { name: "Reads" }).click();
    await reads;

    const system = asked("/queries", "system", "true");
    await page.getByRole("button", { name: "System" }).click();
    await system;

    const program = asked("/live", "program", "checkout-api");
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=live&program=checkout-api");
    await program;
    await expect(page.getByRole("group", { name: "Filters" })).toContainText("checkout-api");
  });

  test("keeps the bar's controls in the window on every tab, the System switch with them", async ({ page }) => {
    await answerApi(page, { look: "forecast", view: "advanced" });

    for (const tab of ["databases", "live"]) {
      await page.goto(`/metrics/databases/microsoft.sql_server/sql-prod-01?tab=${tab}`);
      await expect(page.getByRole("heading", { level: 1, name: "sql-prod-01" })).toBeVisible();

      const width = page.viewportSize()?.width ?? 0;
      const advanced = page.getByRole("group", { name: "View" }).getByRole("button", { name: "Advanced" });
      const system = page.getByRole("button", { name: "System" });

      for (const control of [advanced, system]) {
        const box = await control.boundingBox();
        expect(box).not.toBeNull();
        expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(width);
      }

      // The toggle keeps its place between tabs, as on every screen (VEY-62).
      expect(((await advanced.boundingBox())?.x ?? 0) + ((await advanced.boundingBox())?.width ?? 0)).toBeGreaterThan(
        width - 40,
      );
    }
  });
});
