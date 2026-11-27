import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

const server = "/metrics/databases/microsoft.sql_server/sql-prod-01";

test.describe("Console SQL Server (VEY-75)", () => {
  test("shows the machine, the figures, and every panel of the overview, without scrolling sideways", async ({
    page,
  }) => {
    await answerApi(page, { look: "console" });
    await page.goto(server);

    await expect(page.getByRole("heading", { level: 1, name: /sql-prod-01/ })).toBeVisible();
    await expect(page.getByText("Windows Server 2022 (21H2)")).toBeVisible();
    await expect(page.getByText("16 vCPU · 64 GB RAM")).toBeVisible();

    const figures = page.getByRole("region", { name: "Figures" });
    for (const figure of ["Instance CPU", "SQL memory", "Active sessions", "Blocked sessions", "Database size"]) {
      await expect(figures).toContainText(figure);
    }
    await expect(figures).toContainText("1,842");

    for (const panel of [
      "Instance performance",
      "Core counters",
      "Top wait types",
      "Backups",
      "Blocking",
      "Storage",
      "Database inventory",
      "Alerts and coverage",
    ]) {
      await expect(page.getByRole("region", { name: panel, exact: true })).toBeVisible();
    }

    await expect(page.getByRole("region", { name: "Core counters" })).toContainText("Compilations / s");
    await expect(page.getByRole("region", { name: "Alerts and coverage" })).toContainText(
      "the login needs SQLAgentReaderRole in msdb",
    );

    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });

  test("asks for the overview and the backups in the top bar's range", async ({ page }) => {
    await answerApi(page, { look: "console" });
    const overview = page.waitForRequest((request) => request.url().includes("/sql-prod-01/overview?"));
    const backups = page.waitForRequest((request) => request.url().includes("/sql-prod-01/backups?"));
    await page.goto(`${server}?range=7d`);

    expect(new URL((await overview).url()).searchParams.get("range")).toBe("7d");
    expect(new URL((await backups).url()).searchParams.get("range")).toBe("7d");
  });

  test("opens the blocking chains with the head blocker's tree and statement", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto(`${server}?tab=blocking`);

    const chains = page.getByRole("region", { name: "Active blocking sessions" });
    await expect(chains).toContainText("LCK_M_X");
    await expect(page.getByRole("region", { name: "Session 64" })).toContainText("UPDATE dbo.Orders");

    await chains.getByRole("button", { name: "115" }).click();
    await expect(page.getByRole("region", { name: "Session 115" })).toContainText("DELETE FROM dbo.OrderItems");
  });

  test("shows backup freshness against the policy, the last 7 days, and the history", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto(`${server}?tab=backups`);

    const freshness = page.getByRole("region", { name: "Backup freshness" });
    await expect(freshness).toContainText("ReportingDB");
    await expect(freshness).toContainText("Overdue");
    await expect(freshness).toContainText("Full backup within 26 h");
    await expect(page.getByRole("region", { name: "The last 7 days" })).toContainText("L·96");
    await expect(page.getByRole("region", { name: "Backup history" })).toContainText("SalesDB");
  });

  test("shows storage growth and volume capacity", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto(`${server}?tab=storage`);

    await expect(page.getByRole("region", { name: "Figures" })).toContainText("Growth, 30 days");
    const volumes = page.getByRole("region", { name: "Volume capacity" });
    await expect(volumes).toContainText("D:");
    await expect(volumes).toContainText("11% free");
  });

  test("opens a figure's tab from anywhere on its tile (VEY-82)", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto(server);
    const figures = page.getByRole("region", { name: "Figures" });

    await figures
      .locator(".cn-figure", { hasText: "Blocked sessions" })
      .locator(".cn-figure-value")
      .click({ force: true });
    await expect(page).toHaveURL(/tab=blocking/);
  });
});
