import { expect, test, type Page } from "@playwright/test";

import { openDiskFilling, resolvedLatency } from "../src/alerts/testAlerts";
import { answerApi } from "./api";

/** Whether the page, or the screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const screen = document.querySelector(".alert-screen");

    return (
      document.documentElement.scrollWidth > window.innerWidth ||
      (screen !== null && screen.scrollWidth > screen.clientWidth)
    );
  });
}

test.describe("Alerts screen", () => {
  test("lays out the counts, the weather log, the list, and the rules for the screen size", async ({
    page,
  }, testInfo) => {
    await answerApi(page);
    await page.goto("/alerts");

    await expect(page.getByRole("heading", { level: 1, name: "Alerts" })).toBeVisible();
    await expect(page.getByText(/2 alerts are open\. 23 alerts in the last 30 days/)).toBeVisible();
    await expect(page.getByRole("region", { name: "Weather log · last 30 days" }).getByRole("listitem")).toHaveCount(
      30,
    );
    await expect(page.getByRole("region", { name: "Today" }).getByRole("link")).toHaveCount(3);
    expect(await scrollsSideways(page)).toBe(false);

    const list = await page.getByRole("region", { name: "Alert list" }).boundingBox();
    const rules = await page.getByRole("complementary", { name: "Built-in rules" }).boundingBox();

    if (testInfo.project.name === "desktop") {
      // The rules stand beside the list.
      expect(rules?.x ?? 0).toBeGreaterThan((list?.x ?? 0) + (list?.width ?? 0));
    } else {
      // One column: the rules below the list.
      expect(rules?.y ?? 0).toBeGreaterThan((list?.y ?? 0) + (list?.height ?? 0) - 1);
    }
  });

  test("keeps the filters in the address", async ({ page }) => {
    await answerApi(page);
    await page.goto("/alerts");
    await expect(page.getByRole("region", { name: "Today" })).toBeVisible();

    await page.getByRole("button", { name: /^Resolved/ }).click();
    await page.getByRole("button", { name: /Disk filling/ }).click();

    await expect(page).toHaveURL(/\/alerts\?state=resolved&rule=disk-filling$/);
    await expect(page.getByRole("button", { name: /^Resolved/ })).toHaveAttribute("aria-pressed", "true");
  });

  test("leads from a row to the alert's details and back", async ({ page }) => {
    await answerApi(page);
    await page.goto("/alerts");

    await page.getByRole("link", { name: /Disk \/ on web-01 is full in 9 days/ }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Disk / on web-01 is full in 9 days" })).toBeVisible();
    await page.getByRole("link", { name: "← Alerts" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Alerts" })).toBeVisible();
  });

  test("is reached from the main navigation with the keyboard", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "The main navigation folds into the menu below desktop width.");
    await answerApi(page);
    await page.goto("/");

    await page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Alerts" }).focus();
    await page.keyboard.press("Enter");

    await expect(page.getByRole("heading", { level: 1, name: "Alerts" })).toBeVisible();
  });

  test("works in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page);
    await page.goto("/alerts");
    await expect(page.getByRole("region", { name: "Today" })).toBeVisible();

    const colour = await page.locator(".alerts-hero h1").evaluate((title) => getComputedStyle(title).color);

    expect(colour).toBe("rgb(255, 255, 255)");
  });
});

test.describe("Metric alert details", () => {
  test("shows a disk alert's forecast and the host's disks without scrolling sideways", async ({ page }) => {
    await answerApi(page);
    await page.goto(`/alerts/${openDiskFilling.id}`);

    await expect(page.getByRole("heading", { level: 1, name: "Disk / on web-01 is full in 9 days" })).toBeVisible();
    await expect(page.getByRole("img", { name: /full on Nov 19/ })).toBeVisible();
    await expect(page.getByRole("region", { name: "Disks of web-01" }).getByRole("listitem")).toHaveCount(3);
    expect(await scrollsSideways(page)).toBe(false);

    const chart = await page.locator(".rate-chart svg").boundingBox();
    const panel = await page.locator(".alert-chart").boundingBox();
    expect(chart?.width ?? 0).toBeGreaterThan((panel?.width ?? 0) - 60);
  });

  test("shows a latency alert's durations and its slowest routes in clear skies", async ({ page }) => {
    await answerApi(page);
    await page.goto(`/alerts/${resolvedLatency.id}`);

    await expect(page.getByRole("heading", { level: 1, name: "checkout-web is back to p95 420 ms" })).toBeVisible();
    await expect(page.getByRole("img", { name: /Request durations of checkout-web/ })).toBeVisible();
    await expect(page.getByRole("table").getByRole("row")).toHaveCount(4);
    await expect(page.locator(".shell")).toHaveClass(/scene-clear/);
    expect(await scrollsSideways(page)).toBe(false);
  });
});
