import { expect, test, type Page } from "@playwright/test";

import { clearSnapshot } from "../src/home/testSnapshots";
import { answerApi } from "./api";

/**
 * Whether the page, or the home's own scrolling area, is wider than the window: the usual sign of a layout that
 * breaks on small screens. The hourly strip may scroll sideways inside its panel; nothing else may.
 */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const home = document.querySelector(".home");

    return (
      document.documentElement.scrollWidth > window.innerWidth || (home !== null && home.scrollWidth > home.clientWidth)
    );
  });
}

test.describe("Forecast home", () => {
  test("reads the storm and lays out for the screen size", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto("/");

    const title = page.getByRole("heading", { level: 1, name: "Storm over Services" });
    const pane = page.getByRole("region", { name: "Needs you" });
    await expect(title).toBeVisible();
    await expect(pane).toBeVisible();
    await expect(page.getByRole("status", { name: "Master warning" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    const titleBox = await title.boundingBox();
    const paneBox = await pane.boundingBox();

    if (testInfo.project.name === "desktop") {
      // Side by side: the pane to the right of the forecast.
      expect(paneBox?.x ?? 0).toBeGreaterThan((titleBox?.x ?? 0) + (titleBox?.width ?? 0));
      await expect(page.getByRole("button", { name: /production/, pressed: true })).toBeVisible();
    } else if (testInfo.project.name === "tablet") {
      // One column: the pane below the forecast, the pills folded into a picker.
      expect(paneBox?.y ?? 0).toBeGreaterThan(titleBox?.y ?? 0);
      await expect(page.getByRole("combobox", { name: "Environment" })).toBeVisible();
    } else {
      // Phones: Needs you first.
      expect(paneBox?.y ?? 0).toBeLessThan(titleBox?.y ?? 0);
    }
  });

  test("opens the logs of an alert from Needs you", async ({ page }) => {
    await answerApi(page);
    await page.goto("/");

    await page.getByRole("region", { name: "Needs you" }).getByRole("link", { name: "Open logs" }).click();

    await expect(page).toHaveURL(/\/logs\?severity=Error&service=payments-gateway$/);
    await expect(page.getByRole("heading", { name: "Logs" })).toBeVisible();
  });

  test("opens another alert with the keyboard", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "Phones have no keyboard to tab with.");
    await answerApi(page);
    await page.goto("/");

    const second = page.getByRole("button", { name: /billing-worker/ });
    await second.focus();
    await page.keyboard.press("Enter");

    await expect(second).toHaveAttribute("aria-expanded", "true");
    await expect(page.getByRole("link", { name: "± 1 minute" })).toHaveAttribute("href", /service=billing-worker/);
  });

  test("paints the storm at night in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page);
    await page.goto("/");
    await expect(page.getByRole("heading", { level: 1, name: "Storm over Services" })).toBeVisible();

    const scene = await page.locator(".shell").evaluate((shell) => getComputedStyle(shell).backgroundImage);
    const colour = await page.locator(".fc-hero h1").evaluate((title) => getComputedStyle(title).color);

    expect(scene).toContain("rgb(106, 37, 72)");
    expect(colour).toBe("rgb(255, 255, 255)");
  });

  test("shows clear skies by day in the light theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "light" });
    await answerApi(page, { home: clearSnapshot });
    await page.goto("/");

    await expect(page.getByRole("heading", { level: 1, name: "Clear skies" })).toBeVisible();
    await expect(page.getByText("Nothing needs you")).toBeVisible();

    const scene = await page.locator(".shell").evaluate((shell) => getComputedStyle(shell).backgroundImage);

    expect(scene).toContain("rgb(169, 211, 255)");
    expect(await scrollsSideways(page)).toBe(false);
  });
});
