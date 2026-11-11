import { expect, type Locator, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** The environment choice in the bar: pills on desktop, one picker on tablets and phones. */
function environments(page: Page, project: string) {
  return project === "desktop"
    ? page.getByRole("group", { name: "Environment" })
    : page.getByRole("combobox", { name: /environment/i });
}

test.describe("Shell", () => {
  test("keeps the same bar on every screen", async ({ page }, testInfo) => {
    await answerApi(page);

    for (const path of ["/", "/logs", "/metrics", "/alerts", "/keys"]) {
      await page.goto(path);

      await expect(environments(page, testInfo.project.name)).toBeVisible();
      await expect(page.getByRole("status", { name: "Master warning" })).toBeVisible();
    }
  });

  test("keeps the weather to a band on work screens and in full on the home", async ({ page }) => {
    await answerApi(page);
    await page.goto("/logs");
    await expect(page.locator(".shell")).toHaveClass(/scene-storm/);

    await expect(page.locator(".shell")).toHaveClass(/calm/);
    await expect(page.locator(".shell-veil")).toHaveCSS("opacity", "1");
    await expect(page.locator("canvas.shell-weather")).toBeAttached();

    await page.goto("/");
    await expect(page.locator(".shell")).not.toHaveClass(/calm/);
    await expect(page.locator(".shell-veil")).toHaveCSS("opacity", "0");
  });

  test("blends the home into the next screen", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "The main navigation folds into the menu below desktop width.");
    await answerApi(page);
    await page.goto("/");
    await expect(page.getByRole("region", { name: "Needs you" })).toBeVisible();

    // React Router starts a view transition for the navigation links; the shell's styles animate it.
    const started = page.evaluate(
      () =>
        new Promise<boolean>((resolve) => {
          const original = document.startViewTransition?.bind(document);

          if (!original) {
            resolve(false);

            return;
          }

          document.startViewTransition = ((update: () => void) => {
            resolve(true);

            return original(update);
          }) as typeof document.startViewTransition;
        }),
    );
    await page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Logs" }).click();

    expect(await started).toBe(true);
    await expect(page.getByRole("heading", { name: "Logs" })).toBeVisible();
  });

  test("still shows one frame of weather without motion", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await answerApi(page);
    await page.goto("/");
    await expect(page.getByRole("region", { name: "Needs you" })).toBeVisible();

    const drawn = await page.locator("canvas.shell-weather").evaluate((canvas: HTMLCanvasElement) => {
      const pixels = canvas.getContext("2d")?.getImageData(0, 0, canvas.width, canvas.height).data ?? [];

      return Array.from(pixels).some((value, index) => index % 4 === 3 && value > 0);
    });

    expect(drawn).toBe(true);
  });
});

test.describe("Home on large screens", () => {
  // Natural size on every screen (VEY-24): more to see, never bigger type and never a sideways scroll.
  async function open(page: Page, width: number, height: number) {
    await page.setViewportSize({ width, height });
    await answerApi(page);
    await page.goto("/");
    await expect(page.getByRole("region", { name: "Needs you" })).toBeVisible();
    await expect(page.locator(".home-body")).toHaveCSS("zoom", "1");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }

  async function boxOf(locator: Locator) {
    const box = await locator.boundingBox();

    if (!box) {
      throw new Error("The element is not laid out.");
    }

    return box;
  }

  test("keeps the standard home below 1800 px", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
    await open(page, 1440, 900);

    await expect(page.getByRole("region", { name: "Services and hosts" })).toHaveCount(0);
    await expect(page.getByRole("list", { name: "Hours" })).toBeVisible();
  });

  for (const [width, height] of [
    [1920, 1080],
    [2560, 1080],
  ] as const) {
    test(`adds every service, host, and disk at ${width} × ${height}`, async ({ page }, testInfo) => {
      test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
      await open(page, width, height);

      const blocks = page.getByRole("region", { name: "Services and hosts" });
      await expect(blocks).toBeVisible();
      await expect(page.getByRole("region", { name: "Disks" })).toBeVisible();
      await expect(page.getByRole("region", { name: "Last 24 hours" })).toHaveCount(0);

      // Three columns side by side: the Forecast, the blocks, and Needs you.
      const forecast = await boxOf(page.locator(".forecast"));
      const side = await boxOf(blocks);
      const pane = await boxOf(page.getByRole("region", { name: "Needs you" }));
      expect(forecast.x + forecast.width).toBeLessThanOrEqual(side.x);
      expect(side.x + side.width).toBeLessThanOrEqual(pane.x);
    });
  }

  for (const [width, height] of [
    [2560, 1440],
    [3840, 2160],
  ] as const) {
    test(`fills a wall with the day, the month, and the latest alerts at ${width} × ${height}`, async ({
      page,
    }, testInfo) => {
      test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
      await open(page, width, height);

      const chart = page.getByRole("region", { name: "Last 24 hours" });
      await expect(chart).toBeVisible();
      await expect(page.getByRole("region", { name: "Last 30 days" })).toBeVisible();
      await expect(page.getByRole("region", { name: "Latest alerts" }).getByRole("link").first()).toBeVisible();
      await expect(page.getByRole("list", { name: "Hours" })).toHaveCount(0);

      // The bands reach the foot of the screen without a scroll.
      const box = await boxOf(chart);
      expect(height - (box.y + box.height)).toBeLessThan(60);
      expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight)).toBe(true);
    });
  }
});
