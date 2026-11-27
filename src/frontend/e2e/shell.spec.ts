import { expect, type Locator, test, type Page } from "@playwright/test";

import type { HomeSnapshot } from "../src/api/types";
import { stormSnapshot } from "../src/home/testSnapshots";
import { answerApi } from "./api";

/** The width of the sidebar beside the screen. */
async function sidebarWidth(page: Page): Promise<number> {
  const box = await page.getByRole("navigation", { name: "Main" }).boundingBox();

  return Math.round(box?.width ?? 0);
}

test.describe("Shell", () => {
  test("keeps the environment and the open alerts in view on every screen", async ({ page }) => {
    await answerApi(page, { look: "forecast" });

    for (const path of ["/", "/logs", "/metrics", "/alerts", "/keys"]) {
      await page.goto(path);

      await expect(page.getByRole("button", { name: /^Environment: production/ })).toBeVisible();
      await expect(page.getByRole("link", { name: "Alerts", exact: true })).toHaveAccessibleDescription(
        "1 warning, 1 caution",
      );
    }
  });

  test("shows icons on laptops and tablets and a drawer on phones", async ({ page }, testInfo) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/");
    await expect(page.getByRole("region", { name: "Needs you" })).toBeVisible();

    if (testInfo.project.name === "phone") {
      const main = page.getByRole("navigation", { name: "Main" });
      await expect(main).toBeHidden();
      await page.getByRole("button", { name: "Menu" }).click();
      await expect(main.getByRole("link", { name: "Logs" })).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(main).toBeHidden();
    } else {
      expect(await sidebarWidth(page)).toBe(64);
      await page.getByRole("button", { name: "Expand the sidebar" }).click();
      expect(await sidebarWidth(page)).toBe(232);
      await page.reload();
      await expect(page.getByRole("button", { name: "Collapse the sidebar" })).toBeVisible();
    }
  });

  test("keeps the sidebar in place: every screen scrolls in its own area, not the page", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "On phones the sidebar is a drawer.");
    // Eight full page loads; on a busy CI runner they need more than one test's usual 30 seconds.
    test.slow();
    // Short enough that every screen has more than fits.
    await page.setViewportSize({ width: 1440, height: 420 });
    await answerApi(page, { look: "forecast" });

    for (const path of ["/", "/logs", "/metrics", "/alerts", "/keys", "/users", "/notifications", "/account"]) {
      await page.goto(path);
      await expect(page.getByRole("navigation", { name: "Main" })).toBeVisible();
      await page.waitForTimeout(300);

      const pageScroll = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight);
      expect(pageScroll, path).toBe(0);
    }
  });

  test("leads home from the logo", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/logs");

    await page.getByRole("link", { name: "Veyra home" }).click();

    await expect(page.getByRole("region", { name: "Needs you" })).toBeVisible();
  });

  test("labels the sidebar on large screens", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
    await page.setViewportSize({ width: 1920, height: 1080 });
    await answerApi(page, { look: "forecast" });
    await page.goto("/");

    expect(await sidebarWidth(page)).toBe(232);
    await expect(page.getByRole("link", { name: "Metrics" })).toContainText("Metrics");
  });

  test("chooses the environment from the switcher", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/");

    await page.getByRole("button", { name: /^Environment:/ }).click();
    const list = page.getByRole("dialog", { name: "Choose an environment" });
    await expect(list.getByRole("searchbox", { name: "Find an environment" })).toBeFocused();
    await list.getByRole("button", { name: /All environments/ }).click();

    await expect(page.getByRole("button", { name: /^Environment: All environments/ })).toBeVisible();
  });

  test("asks the server for the environments the find box keeps", async ({ page }) => {
    // The server filters the switcher's list (VEY-67), so the search must reach it.
    await answerApi(page, { look: "forecast" });
    await page.goto("/");

    await page.getByRole("button", { name: /^Environment:/ }).click();
    const list = page.getByRole("dialog", { name: "Choose an environment" });
    const asked = page.waitForRequest((request) => {
      const url = new URL(request.url());

      return url.pathname === "/api/v1/home/environments" && url.searchParams.get("search") === "prod";
    });
    await list.getByRole("searchbox", { name: "Find an environment" }).fill("prod");
    await asked;

    await expect(list.getByRole("button", { name: /production/ })).toBeVisible();
    await expect(list.getByRole("button", { name: /All environments/ })).toHaveCount(0);
  });

  test("keeps the weather to a band on work screens and in full on the home", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
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
    test.skip(testInfo.project.name === "phone", "On phones the main navigation waits in the drawer.");
    await answerApi(page, { look: "forecast" });
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
    await answerApi(page, { look: "forecast" });
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
  async function open(page: Page, width: number, height: number, home?: HomeSnapshot) {
    await page.setViewportSize({ width, height });
    await answerApi(page, { look: "forecast", home });
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

  // The home is its own scroll area beside the sidebar, so the page itself never scrolls; what counts is the home.
  // The sizes are browser windows: 1440 × 900 and 1536 × 864 laptops, 1080p, 1440p, and 4K screens, less the
  // browser's own bars.
  for (const [width, height] of [
    [1440, 790],
    [1536, 730],
    [1920, 945],
    [2560, 1305],
    [3840, 2025],
  ] as const) {
    test(`fits the home without scrolling in a ${width} × ${height} browser`, async ({ page }, testInfo) => {
      test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
      await open(page, width, height);

      const overflow = await page.locator(".home").evaluate((home) => home.scrollHeight - home.clientHeight);
      expect(overflow).toBe(0);
    });

    // Without records in the last five minutes the hero says so in words, which must not grow the column.
    test(`fits the home without records in a ${width} × ${height} browser`, async ({ page }, testInfo) => {
      test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
      await open(page, width, height, {
        ...stormSnapshot,
        condition: { ...stormSnapshot.condition, errorRatio: null },
        metrics: { ...stormSnapshot.metrics, errorRatio: null, records: 0, ingestPerSecond: 0 },
      });

      await expect(page.getByTestId("hero-ratio")).toHaveText(/No records/);
      const overflow = await page.locator(".home").evaluate((home) => home.scrollHeight - home.clientHeight);
      expect(overflow).toBe(0);
    });
  }

  test("keeps the estate beside the outlook below 1800 px", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
    await open(page, 1440, 900);

    await expect(page.locator(".forecast").getByRole("region", { name: "Services and hosts" })).toBeVisible();
    await expect(page.getByRole("list", { name: "Hours" })).toBeVisible();
  });

  for (const [width, height] of [
    [1920, 1080],
    [2560, 1080],
  ] as const) {
    test(`gives the estate a column of its own at ${width} × ${height}`, async ({ page }, testInfo) => {
      test.skip(testInfo.project.name !== "desktop", "Large screens are desktop browsers.");
      await open(page, width, height);

      const blocks = page.getByRole("region", { name: "Services and hosts" });
      await expect(blocks).toBeVisible();
      await expect(page.getByRole("region", { name: "Disks" })).toHaveCount(0);
      await expect(page.getByRole("region", { name: "Last 24 hours" })).toHaveCount(0);

      // Three columns side by side: the Forecast, the blocks, and Needs you.
      const forecast = await boxOf(page.locator(".forecast"));
      const side = await boxOf(blocks);
      const pane = await boxOf(page.getByRole("region", { name: "Needs you" }));
      expect(forecast.x + forecast.width).toBeLessThanOrEqual(side.x);
      expect(side.x + side.width).toBeLessThanOrEqual(pane.x);

      // Beside the labelled sidebar the strip is narrower; every hour must still be read in full.
      const cut = await page
        .locator(".fc-hour-value")
        .evaluateAll(
          (values) => values.filter((value) => value.scrollWidth > (value.parentElement?.clientWidth ?? 0)).length,
        );
      expect(cut).toBe(0);
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
      expect(await page.locator(".home").evaluate((home) => home.scrollHeight - home.clientHeight)).toBe(0);
    });
  }

  test("shows the version, which opens the release notes, in the Forecast look too (VEY-82)", async ({
    page,
  }, testInfo) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/logs");

    // A phone keeps the menu, and the version at its foot, in a drawer.
    if (testInfo.project.name === "phone") {
      await page.getByRole("button", { name: "Menu" }).click();
    }

    await page.getByRole("button", { name: "Veyra 0.4.1: what's new" }).click();
    const notes = page.getByRole("dialog", { name: "What's new in Veyra" });
    await expect(notes).toContainText("Tiles open their details.");
    await notes.getByRole("button", { name: "Close" }).click();
    await expect(notes).toHaveCount(0);
  });
});
