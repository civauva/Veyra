import { expect, test, type Page } from "@playwright/test";

import { checkoutId } from "../src/traces/testTraces";
import { answerApi } from "./api";

/** Whether the page, or the screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page, selector: string): Promise<boolean> {
  return page.evaluate((area) => {
    const screen = document.querySelector(area);

    return (
      document.documentElement.scrollWidth > window.innerWidth ||
      (screen !== null && screen.scrollWidth > screen.clientWidth)
    );
  }, selector);
}

test.describe("Traces", () => {
  test("lists traces with the chart and opens one at every size", async ({ page }) => {
    await answerApi(page);
    await page.goto("/traces");

    await expect(page.getByRole("img", { name: /3 traces: 1 failed/ })).toBeVisible();
    const row = page.getByRole("row", { name: /POST \/api\/checkout/ });
    await expect(row).toContainText("psp-client: The PSP did not answer");
    expect(await scrollsSideways(page, ".traces-main")).toBe(false);

    await row.click();

    await expect(page.getByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toBeVisible();
    await expect(page.getByRole("status")).toContainText("Part of this trace has not arrived");
    expect(await scrollsSideways(page, ".trace-main")).toBe(false);
  });

  test("shows the cause beside the waterfall, or as a sheet on phones", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto(`/traces/${checkoutId}?at=2026-11-09T12:00:00.000Z`);

    const panel = page.getByRole("complementary", { name: "POST /authorize" });

    if (testInfo.project.name === "phone") {
      // On phones the waterfall comes first; the span opens as a sheet when tapped.
      await expect(panel).toHaveCount(0);
      await page.getByRole("button", { name: "POST /authorize" }).click();
    }

    await expect(panel).toBeVisible();
    await expect(panel.getByText("System.TimeoutException")).toBeVisible();
    await expect(panel.getByText("Authorization failed: TimeoutException after 1500 ms")).toBeVisible();

    const box = await panel.boundingBox();
    const viewport = page.viewportSize();

    if (testInfo.project.name === "phone") {
      expect(Math.round((box?.y ?? 0) + (box?.height ?? 0))).toBe(viewport?.height);
    } else {
      expect(Math.round((box?.x ?? 0) + (box?.width ?? 0))).toBe(viewport?.width);
    }
  });

  test("goes from a log record to its trace", async ({ page }) => {
    await answerApi(page);
    await page.goto("/logs");

    await page
      .getByRole("button", { name: /Payment declined by PSP/ })
      .first()
      .click();
    await expect(page.getByRole("link", { name: "Open trace" })).toHaveAttribute(
      "href",
      /\/traces\/5b8efff798038103d269b633813fc60c\?at=/,
    );
  });
});
