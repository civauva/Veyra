import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

/*
 * Every meaningful element leads to its details, and every warning to what it means and how to fix it (VEY-83). The
 * tiles' names are stretched over the tiles, so a click on the figure itself is forced: Playwright sees the value as
 * covered by the link, which is the point.
 */
test.describe("Drill-in (VEY-83)", () => {
  test("opens a database server's tabs from the Databases figures and cells", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/resources/databases?range=7d");
    const figures = page.getByRole("region", { name: "Figures" });

    await figures.getByRole("link", { name: "Backups late" }).click();
    await expect(page).toHaveURL(/\/metrics\/databases\/[^/]+\/[^/?]+\?tab=backups&range=7d$/);

    await page.goto("/resources/databases");
    await figures.locator(".rs-figure", { hasText: "Blocking now" }).locator(".rs-figure-value").click({ force: true });
    await expect(page).toHaveURL(/\?tab=blocking$/);
  });

  test("opens the Overview at its databases, scrolled to and pulsing", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/resources/databases");

    await page.getByRole("region", { name: "Figures" }).getByRole("link", { name: "Databases", exact: true }).click();

    await expect(page).toHaveURL(/\/#databases$/);
    const panel = page.getByRole("region", { name: "SQL Server databases" });
    await expect(panel).toHaveClass(/pulse-once/);
    await expect(panel).toBeInViewport();
  });

  test("leads a late backup to what it means and how to fix it", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=backups");
    const freshness = page.getByRole("region", { name: "Backup freshness" });

    await freshness
      .getByRole("link", { name: /^(Overdue|Due)$/ })
      .first()
      .click();

    await expect(page).toHaveURL(/\/alerts\/rules\/db-backup-age\?kind=database&.*#fix$/);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    const fix = page.getByRole("region", { name: /How to fix/ });
    await expect(fix).toHaveClass(/pulse-once/);
    await expect(fix.getByRole("listitem").first()).toBeVisible();
  });

  test("opens a host's warning alerts from its health pill", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/resources/hosts");
    const list = page.getByRole("region", { name: "Hosts & containers" });

    await list
      .getByRole("link", { name: /^(Warning|Caution)$/ })
      .first()
      .click();

    await expect(page).toHaveURL(/\/alerts\?state=open&q=/);
  });
});
