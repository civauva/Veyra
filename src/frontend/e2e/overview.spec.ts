import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

test.describe("Console Overview (VEY-73)", () => {
  test("shows the figures, the panels, and the tables of the reference, without scrolling sideways", async ({
    page,
  }) => {
    await answerApi(page, { look: "console" });
    // Every panel of the reference shows in the Advanced view; Simple keeps Veyra's plumbing out (VEY-83).
    await page.addInitScript(() => localStorage.setItem("veyra.view", "advanced"));
    await page.goto("/");

    const figures = page.getByRole("region", { name: "Figures" });
    await expect(figures).toContainText("1.24M");
    await expect(figures).toContainText("99.95%");

    for (const panel of [
      "Needs attention",
      "Request activity",
      "Telemetry ingestion",
      "Applications",
      "Infrastructure",
      "SQL Server databases",
      "Pipeline executions",
      "Host resource trends",
      "Collector health",
    ]) {
      await expect(page.getByRole("region", { name: panel })).toBeVisible();
    }

    await expect(page.getByRole("region", { name: "SQL Server databases" })).toContainText("Backup late");
    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });

  test("shows every column of its tables on a desktop and a tablet (VEY-80)", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "A phone scrolls the tables sideways inside their panels.");
    await answerApi(page, { look: "console" });

    for (const width of [testInfo.project.name === "tablet" ? 1024 : 1440, 1920]) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/");
      await expect(page.getByRole("region", { name: "Applications" })).toContainText("payments-gateway");
      await expect(page.getByRole("region", { name: "SQL Server databases" })).toContainText("Backup late");

      const cut = await page.evaluate(() =>
        [...document.querySelectorAll(".ov-scroll")]
          .filter((box) => box.scrollWidth > box.clientWidth + 1)
          .map((box) => box.closest("section")?.getAttribute("aria-labelledby")),
      );
      expect(cut, `at ${width} pixels`).toEqual([]);
    }
  });

  test("fills a large screen and asks for as many rows as fit (VEY-81)", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "The large screens are set here; tablets and phones scroll.");
    await answerApi(page, { look: "console" });
    await page.setViewportSize({ width: 2560, height: 2160 });
    const asked: string[] = [];
    page.on("request", (request) => asked.push(request.url()));
    await page.goto("/");
    await expect(page.getByRole("region", { name: "Applications" })).toContainText("payments-gateway");

    await expect
      .poll(() =>
        asked.some(
          (url) => url.includes("/api/v1/resources/services?") && Number(new URL(url).searchParams.get("limit")) > 8,
        ),
      )
      .toBe(true);
    await expect
      .poll(() =>
        asked.some(
          (url) => url.includes("/api/v1/overview?") && Number(new URL(url).searchParams.get("pipelines")) > 8,
        ),
      )
      .toBe(true);

    const band = await page.evaluate(() => {
      const screen = document.querySelector(".ov");
      const last = [...(screen?.querySelectorAll(":scope > .ov-row") ?? [])].at(-1);

      if (!screen || !last) {
        return Number.POSITIVE_INFINITY;
      }

      const foot = screen.getBoundingClientRect().bottom - parseFloat(getComputedStyle(screen).paddingBottom);

      return foot - last.getBoundingClientRect().bottom;
    });
    expect(band).toBeLessThan(2);
  });

  test("opens the details behind a tile, a chart, and a row, keeping the range (VEY-82)", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/?range=7d");
    const figures = page.getByRole("region", { name: "Figures" });
    await expect(figures).toContainText("1.24M");

    await figures.locator(".ov-figure", { hasText: "Error rate" }).locator(".ov-figure-value").click({ force: true });
    await expect(page).toHaveURL(/\/traces\?show=failed&range=7d$/);

    await page.goto("/?range=7d");
    await page.getByRole("region", { name: "Host resource trends" }).locator("svg").first().click({ force: true });
    await expect(page).toHaveURL(/\/resources\/hosts\?range=7d$/);

    await page.goto("/?range=7d");
    const applications = page.getByRole("region", { name: "Applications" });
    await applications
      .getByRole("row", { name: /payments-gateway/ })
      .locator("td")
      .last()
      .click({ force: true });
    await expect(page).toHaveURL(/\/metrics\/services\/payments-gateway/);
  });

  test("opens an alert at how to fix it, scrolled into view and pulsing (VEY-83)", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/");

    await page.getByRole("link", { name: "What it means and how to fix it →" }).click();

    await expect(page).toHaveURL(/\/alerts\/[^/]+#fix$/);
    const fix = page.locator("#fix");
    await expect(fix).toHaveClass(/pulse-once/);
    await expect(fix).toBeInViewport();
  });

  test("shows the moment under the pointer on a chart, and still opens the panel on a click (VEY-83)", async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "A phone has no pointer to hover with.");
    await answerApi(page, { look: "console" });
    await page.goto("/");
    const activity = page.getByRole("region", { name: "Request activity" });
    const plot = activity.locator(".line-chart-plot");
    await expect(plot).toBeVisible();

    const box = await plot.boundingBox();
    expect(box).not.toBeNull();
    await page.mouse.move((box?.x ?? 0) + (box?.width ?? 0) * 0.6, (box?.y ?? 0) + (box?.height ?? 0) / 2);
    const tip = activity.getByRole("status");
    await expect(tip).toContainText("Requests");
    await expect(tip).toContainText("Server errors");

    await page.getByRole("region", { name: "Telemetry ingestion" }).locator(".bars-plot").hover();
    await expect(page.getByRole("region", { name: "Telemetry ingestion" }).getByRole("status")).toContainText("Logs");

    await plot.click();
    await expect(page).toHaveURL(/\/traces/);
  });

  test("keeps Veyra's plumbing to the Advanced view, chosen from the lens (VEY-83)", async ({ page }) => {
    await answerApi(page, { look: "console", view: "simple" });
    await page.goto("/");
    await expect(page.getByRole("region", { name: "Applications" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Collector health" })).toBeHidden();
    await expect(page.getByRole("region", { name: "Telemetry ingestion" })).toBeHidden();

    await page.getByRole("button", { name: "Simple view" }).click();
    await page.getByRole("menuitemradio", { name: /^Advanced/ }).click();

    await expect(page.getByRole("region", { name: "Collector health" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Telemetry ingestion" })).toBeVisible();
  });

  test("asks the server once for the whole Overview, in the shared range", async ({ page }) => {
    await answerApi(page, { look: "console" });
    const asked = page.waitForRequest((request) => request.url().includes("/api/v1/overview?"));
    await page.goto("/?range=7d");

    const url = new URL((await asked).url());
    const days =
      (Date.parse(url.searchParams.get("to") ?? "") - Date.parse(url.searchParams.get("from") ?? "")) / 86_400_000;
    expect(Math.round(days)).toBe(7);
  });
});
