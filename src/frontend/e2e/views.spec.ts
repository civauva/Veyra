import { expect, test, type Page } from "@playwright/test";

import { checkoutId } from "../src/traces/testTraces";
import { answerApi } from "./api";

/** Whether the page is wider than the window, the usual sign of a layout that breaks on small screens. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
}

/** The Simple view of the monitoring screens, at every size (VEY-37). */
test.describe("Simple view", () => {
  test("shows a host's fundamentals and switches to every panel", async ({ page }) => {
    await answerApi(page, { view: "simple" });
    await page.goto("/metrics/hosts/web-01");

    await expect(page.getByRole("region", { name: "Now" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Disks" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Network" })).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);

    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Advanced" }).click();

    await expect(page.getByRole("region", { name: "Network" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Now" })).toBeVisible();
  });

  test("keeps the Log Explorer to its fundamentals", async ({ page }, testInfo) => {
    await answerApi(page, { view: "simple" });
    await page.goto("/logs");

    if (testInfo.project.name === "phone") {
      await page.getByRole("button", { name: /^Filters/ }).click();
    }

    await expect(page.getByRole("region", { name: "At least" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add attribute filter" })).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("keeps a trace to its spans and logs", async ({ page }) => {
    await answerApi(page, { view: "simple" });
    await page.goto(`/traces/${checkoutId}`);

    await expect(page.getByRole("tab", { name: "Spans" })).toBeVisible();
    await expect(page.getByRole("tab", { name: "Attributes" })).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("keeps the alerts to the list, and an alert to its facts and chart", async ({ page }) => {
    await answerApi(page, { view: "simple" });
    await page.goto("/alerts");

    await expect(page.getByRole("region", { name: "Alert list" })).toBeVisible();
    await expect(page.getByRole("complementary", { name: "Built-in rules" })).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);

    await page.getByRole("region", { name: "Alert list" }).getByRole("link").first().click();

    await expect(page.getByText("Peak", { exact: true }).first()).toBeVisible();
    await expect(page.getByRole("region", { name: "Timeline" })).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);
  });
});
