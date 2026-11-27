import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page or the list scrolls sideways, which no screen may do. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
}

test.describe("Resources (VEY-72)", () => {
  for (const look of ["console", "forecast"] as const) {
    test(`lists services worst first, with the server's figures, in the ${look} look`, async ({ page }) => {
      await answerApi(page, { look });
      await page.goto("/resources/services");

      await expect(page.getByRole("heading", { level: 1, name: /^Services/ })).toBeVisible();
      await expect(page.getByRole("region", { name: "Figures" })).toContainText("1.24M");
      const rows = page.getByRole("row");
      await expect(rows.nth(1)).toContainText("payments-gateway");
      await expect(rows.nth(2)).toContainText("Silent");
      expect(await scrollsSideways(page)).toBe(false);
    });
  }

  test("keeps silent hosts listed and names the disk that fills first", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/resources/hosts");

    await expect(page.getByRole("link", { name: "branch-01" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Figures" })).toContainText("sql-01 D: full in ~38 h");
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("sends a filter to the server and keeps it in the address", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/resources/databases");
    await expect(page.getByRole("link", { name: "SQL-PROD-01", exact: true })).toBeVisible();

    const asked = page.waitForRequest(
      (request) =>
        request.url().includes("/api/v1/resources/databases?") && request.url().includes("engine=postgresql"),
    );
    await page.getByRole("combobox", { name: /Engine/ }).selectOption("postgresql");
    await asked;
    await expect(page).toHaveURL(/engine=postgresql/);
  });

  test("opens the Resources screens from the Console menu, with the range kept", async ({ page }, testInfo) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs?range=7d");

    const main = page.getByRole("navigation", { name: "Main" });

    if (testInfo.project.name === "phone") {
      await page.getByRole("button", { name: "Menu" }).click();
    }

    await main.getByRole("link", { name: "Hosts & containers" }).click();
    await expect(page).toHaveURL(/\/resources\/hosts\?range=7d$/);
  });
});
