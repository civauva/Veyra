import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

test.describe("Console Alerts (VEY-73)", () => {
  test("lists the alerts as a table without the weather, and filters on the server", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/alerts");

    await expect(page.getByRole("heading", { level: 1, name: /^Alerts/ })).toBeVisible();
    await expect(page.getByRole("region", { name: "Alerts per day" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Built-in rules" })).toBeVisible();
    await expect(page.getByText("Weather log")).toHaveCount(0);

    const list = page.getByRole("region", { name: "Alert list" });
    await expect(list.getByRole("link").first()).toBeVisible();

    const asked = page.waitForRequest(
      (request) => request.url().includes("/api/v1/alerts?") && request.url().includes("state=open"),
    );
    await list.getByRole("button", { name: /^Open/ }).click();
    await asked;
    await expect(page).toHaveURL(/state=open/);

    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });

  test("opens the alerts behind a figure from anywhere on its tile (VEY-82)", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/alerts");
    const figures = page.getByRole("region", { name: "Figures" });
    await expect(figures.getByRole("button", { name: "Resolved", exact: true })).toBeVisible();

    const asked = page.waitForRequest(
      (request) => request.url().includes("/api/v1/alerts?") && request.url().includes("state=resolved"),
    );
    // A click on the figure itself, not on its name, opens it too. The name's link covers the tile, so Playwright
    // would see the value as hidden under it: force clicks the spot as a person would.
    await figures
      .locator(".cn-figure", { has: page.getByRole("button", { name: "Resolved", exact: true }) })
      .locator(".cn-figure-value")
      .click({ force: true });
    await asked;
    await expect(page).toHaveURL(/state=resolved/);
  });
});
