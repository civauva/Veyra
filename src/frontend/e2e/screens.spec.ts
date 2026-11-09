import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page is wider than the window, the usual sign of a layout that breaks on small screens. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
}

test.describe("Log Explorer", () => {
  test("shows records and opens one without breaking the layout", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto("/logs");

    const row = page.getByRole("button", { name: /Payment declined by PSP: upstream timeout after 1800 ms \(#0\)/ });
    await expect(row).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await row.click();

    const detail = page.getByRole("region", { name: "Selected record" });
    await expect(detail.getByText(/System.TimeoutException/)).toBeVisible();
    const box = await detail.boundingBox();
    const viewport = page.viewportSize();

    if (testInfo.project.name === "phone") {
      // Full screen on phones.
      expect(box?.width).toBe(viewport?.width);
    } else {
      // Beside or over the list, which stays partly visible.
      expect(box?.width ?? 0).toBeLessThan((viewport?.width ?? 0) / 2);
    }

    await page.keyboard.press("Escape");
    await expect(detail).toBeHidden();
  });

  test("filters by severity from the panel or, on phones, the filter sheet", async ({ page }, testInfo) => {
    const searches = await answerApi(page);
    await page.goto("/logs");
    await expect(page.getByRole("button", { name: /Order 88001/ })).toBeVisible();

    if (testInfo.project.name === "phone") {
      await page.getByRole("button", { name: "Filters" }).click();
    } else {
      await expect(page.getByRole("button", { name: "Filters" })).toBeHidden();
    }

    await page.getByRole("button", { name: "Warning", exact: true }).click();

    if (testInfo.project.name === "phone") {
      await page.getByRole("button", { name: "Done" }).click();
    }

    await expect(page).toHaveURL(/severity=Warning/);
    await expect.poll(() => new URL(searches.at(-1)?.url() ?? "http://x").searchParams.get("severity")).toBe("Warning");
  });

  test("loads the next page when scrolled to the end", async ({ page }) => {
    const searches = await answerApi(page);
    await page.goto("/logs");
    await expect(page.getByText(/Showing 100 of/)).toBeVisible();

    await page.locator(".log-list").evaluate((list) => list.scrollTo(0, list.scrollHeight));

    await expect(page.getByText(/Showing 150 of/)).toBeVisible();
    expect(searches.some((search) => search.url().includes("cursor=page-2"))).toBe(true);
  });

  test("moves the selection with the arrow keys", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "Keyboard selection is checked where the list stays in view.");
    await answerApi(page);
    await page.goto("/logs");

    await page.getByRole("button", { name: /\(#0\)/ }).click();
    await page.keyboard.press("ArrowDown");

    await expect(page.getByRole("button", { name: /Order 88001/ })).toHaveAttribute("aria-current", "true");
    await expect(page.getByRole("button", { name: /Order 88001/ })).toBeFocused();
  });

  test("works in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page);
    await page.goto("/logs");

    const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);

    expect(background).toBe("rgb(11, 16, 34)");
  });
});

test.describe("Ingestion keys", () => {
  test("lists keys without breaking the layout", async ({ page }) => {
    await answerApi(page);
    await page.goto("/keys");

    await expect(page.getByRole("heading", { name: "Ingestion keys" })).toBeVisible();
    await expect(page.getByText("web-01 collector")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });
});
