import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page, or the screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const screen = document.querySelector(".metrics");

    return (
      document.documentElement.scrollWidth > window.innerWidth ||
      (screen !== null && screen.scrollWidth > screen.clientWidth)
    );
  });
}

test.describe("Metrics", () => {
  test("asks the server for the cards of the search and the environment", async ({ page }) => {
    // The server keeps the cards that match before it reads them (VEY-67), so each choice must reach it.
    await answerApi(page, { look: "forecast" });
    const asked = (name: string, value: string) =>
      page.waitForRequest((request) => {
        const url = new URL(request.url());

        return url.pathname === "/api/v1/metrics/overview" && url.searchParams.get(name) === value;
      });

    const environment = asked("environment", "production");
    await page.goto("/metrics");
    await environment;

    const search = asked("search", "web");
    await page.getByRole("searchbox", { name: "Find a host or service" }).fill("web");
    await search;
    await expect(page).toHaveURL(/search=web/);
  });

  test("lays the host cards out for the screen size", async ({ page }, testInfo) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/metrics");

    const hosts = page.getByRole("region", { name: "Hosts" }).getByRole("article");
    await expect(hosts).toHaveCount(3);
    expect(await scrollsSideways(page)).toBe(false);

    const first = await hosts.nth(0).boundingBox();
    const second = await hosts.nth(1).boundingBox();
    const third = await hosts.nth(2).boundingBox();
    const sameRow = (a: typeof first, b: typeof first) => Math.abs((a?.y ?? 0) - (b?.y ?? 0)) < 2;

    if (testInfo.project.name === "desktop") {
      expect(sameRow(first, third)).toBe(true);
    } else if (testInfo.project.name === "tablet") {
      expect(sameRow(first, second)).toBe(true);
      expect(sameRow(first, third)).toBe(false);
    } else {
      expect(sameRow(first, second)).toBe(false);
    }
  });

  test("opens a host and draws its charts at every size", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/metrics");

    await page.getByRole("link", { name: /^web-01/ }).click();

    await expect(page.getByRole("heading", { level: 1, name: "web-01" })).toBeVisible();
    await expect(page.getByRole("meter", { name: "/ in use" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    // Each chart fills its panel.
    const holder = page.locator(".metric-panel", { has: page.locator(".line-chart") }).first();
    const chart = await holder.locator(".line-chart svg").first().boundingBox();
    const panel = await holder.boundingBox();
    expect(chart?.width ?? 0).toBeGreaterThan((panel?.width ?? 0) - 40);
  });

  test("leads from a container card to its row on its host, at every size", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/metrics");

    const containers = page.getByRole("region", { name: "Containers" });
    await expect(containers.getByRole("article")).toHaveCount(3);
    expect(await scrollsSideways(page)).toBe(false);

    await containers.getByRole("link", { name: /^shop-worker/ }).click();

    await expect(page.getByRole("heading", { level: 1, name: "web-01" })).toBeVisible();
    const row = page.locator("#container-shop-worker");
    await expect(row).toHaveClass(/picked/);
    await expect(row).toBeInViewport();
    await expect(page.getByRole("meter", { name: "shop-worker memory of its limit" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Logs of shop-worker" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("opens a service and its routes", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/metrics/services/payments-gateway?environment=production");

    await expect(page.getByRole("heading", { level: 1, name: "payments-gateway" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Routes" }).getByText("/checkout/confirm")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("charts a metric from the browser with the keyboard", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "Phones have no keyboard to tab with.");
    await answerApi(page, { look: "forecast" });
    await page.goto("/metrics?tab=all");

    const metric = page.getByRole("button", { name: /http.server.request.duration/ });
    await metric.focus();
    await page.keyboard.press("Enter");

    await expect(page.getByRole("img", { name: /http.server.request.duration p95/ })).toBeVisible();
    await expect(page.getByRole("button", { name: "service.name" })).toHaveAttribute("aria-pressed", "true");
  });

  test("keeps the cards readable in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page, { look: "forecast" });
    await page.goto("/metrics");

    const card = page.getByRole("article", { name: "web-01" });
    await expect(card).toBeVisible();

    const background = await card.evaluate((element) => getComputedStyle(element).backgroundColor);
    const ring = await card
      .locator(".usage-ring.warn .ring-fill")
      .evaluate((element) => getComputedStyle(element).stroke);

    // The dark panel, a little translucent so the weather band shows through on work screens (VEY-23).
    expect(background).toBe("rgba(16, 24, 51, 0.78)");
    expect(ring).toBe("rgb(255, 197, 85)");
  });
});

test.describe("Wide windows", () => {
  for (const [name, path] of [
    ["the Metrics overview", "/metrics"],
    ["a database server", "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=waits"],
    ["Notifications", "/notifications"],
  ] as const) {
    test(`scrolls ${name} at the window's edge, its content in the middle`, async ({ page }, testInfo) => {
      test.skip(testInfo.project.name !== "desktop", "Only a wide window has room beside the content.");
      await page.setViewportSize({ width: 2000, height: 900 });
      await answerApi(page, { look: "forecast" });
      await page.goto(path);

      const area = page.locator("main").first();
      await expect(area).toBeVisible();
      const box = await area.boundingBox();
      expect((box?.x ?? 0) + (box?.width ?? 0)).toBeGreaterThan(1995);
    });
  }
});
