import { expect, test, type Page } from "@playwright/test";

import { resolvedCaution } from "../src/alerts/testAlerts";
import { answerApi } from "./api";

const path = "/alerts/0193a2b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b";

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

test.describe("Alert details", () => {
  test("lays out the alert for the screen size", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto(path);

    const title = page.getByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" });
    const timeline = page.getByRole("region", { name: "Timeline" });
    const errors = page.getByRole("region", { name: /Most frequent errors/ });
    await expect(title).toBeVisible();
    await expect(page.getByRole("img", { name: /Error rate of payments-gateway/ })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    const timelineBox = await timeline.boundingBox();
    const errorsBox = await errors.boundingBox();

    if (testInfo.project.name === "desktop") {
      // Timeline and errors side by side.
      expect(errorsBox?.x ?? 0).toBeGreaterThan((timelineBox?.x ?? 0) + (timelineBox?.width ?? 0));
    } else {
      // One column: the errors below the timeline.
      expect(errorsBox?.y ?? 0).toBeGreaterThan((timelineBox?.y ?? 0) + (timelineBox?.height ?? 0) - 1);
    }

    // The chart fills its panel at every size.
    const chart = await page.locator(".rate-chart svg").boundingBox();
    const panel = await page.locator(".alert-chart").boundingBox();
    expect(chart?.width ?? 0).toBeGreaterThan((panel?.width ?? 0) - 60);
  });

  test("leads into the logs of a frequent error", async ({ page }) => {
    await answerApi(page);
    await page.goto(path);

    await page.getByRole("link", { name: "Logs with “Payment authorisation failed: upstream returned 503”" }).click();

    await expect(page).toHaveURL(/\/logs\?.*service=payments-gateway/);
    await expect(page.getByRole("heading", { name: "Logs" })).toBeVisible();
  });

  test("is reached from Needs you with the keyboard", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "Phones have no keyboard to tab with.");
    await answerApi(page);
    await page.goto("/");

    const details = page.getByRole("region", { name: "Needs you" }).getByRole("link", { name: "Details" });
    await details.focus();
    await page.keyboard.press("Enter");

    await expect(page.getByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" })).toBeVisible();
  });

  test("paints the storm in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page);
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();

    const scene = await page.locator(".shell").evaluate((shell) => getComputedStyle(shell).backgroundImage);
    const colour = await page.locator(".alert-hero h1").evaluate((title) => getComputedStyle(title).color);

    expect(scene).toContain("rgb(106, 37, 72)");
    expect(colour).toBe("rgb(255, 255, 255)");
  });

  test("shows clear skies for a resolved alert in the light theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "light" });
    await answerApi(page, { alert: resolvedCaution });
    await page.goto(path);

    await expect(page.getByRole("heading", { level: 1, name: "orders-api is back to 0.4% errors" })).toBeVisible();

    const scene = await page.locator(".shell").evaluate((shell) => getComputedStyle(shell).backgroundImage);

    expect(scene).toContain("rgb(169, 211, 255)");
    expect(await scrollsSideways(page)).toBe(false);
  });
});
