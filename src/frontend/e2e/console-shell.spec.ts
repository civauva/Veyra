import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

test.describe("Console top bar (VEY-72)", () => {
  test("keeps one time range across screens and in the address", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "The phone shows the range as an icon only; the menu is the same.");
    await answerApi(page, { look: "console" });
    await page.goto("/logs");

    await page.getByRole("button", { name: /^Time range:/ }).click();
    await page.getByRole("dialog", { name: "Time range" }).getByRole("button", { name: "Last 7 days" }).click();
    await expect(page).toHaveURL(/range=7d/);

    await page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Traces" }).click();
    await expect(page).toHaveURL(/\/traces\?range=7d$/);
    await expect(page.getByRole("button", { name: /^Time range:/ })).toContainText("Last 7 days");
  });

  test("searches every signal and opens what it finds", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/");

    await page.getByRole("combobox", { name: /^Search logs, traces/ }).fill("pay");
    const results = page.getByRole("listbox", { name: "Search results" });
    await expect(results.getByRole("group", { name: "Services" })).toContainText("payments-gateway");
    await expect(results.getByRole("group", { name: "Logs" })).toContainText("4,812 in the range");

    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL(/\/metrics\/services\/payments-gateway/);
  });

  test("shows the keyboard shortcuts and switches Live with the keyboard", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "Phones have no keyboard shortcuts to show.");
    await answerApi(page, { look: "console" });
    await page.goto("/logs");
    await expect(page.getByRole("banner").getByRole("link", { name: "Open alerts" })).toBeVisible();

    await page.keyboard.press("?");
    const dialog = page.getByRole("dialog", { name: "Keyboard shortcuts" });
    await expect(dialog).toContainText("Time range");
    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();

    await page.keyboard.press("l");
    const live = page.getByRole("button", { name: /^Live/ });
    await expect(live).toHaveAttribute("aria-pressed", "true");
    await expect(live).toContainText("248/s");
    await expect(page.getByRole("button", { name: /^Refresh:/ })).toContainText("Live");
  });

  test("keeps the refresh interval in this browser", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "The phone leaves the refresh menu out of its top bar.");
    await answerApi(page, { look: "console" });
    await page.goto("/metrics");

    await page.getByRole("button", { name: /^Refresh:/ }).click();
    await page.getByRole("dialog", { name: "Refresh" }).getByRole("button", { name: "Every 10 s" }).click();
    await page.reload();
    await expect(page.getByRole("button", { name: /^Refresh:/ })).toHaveAttribute("aria-expanded", "false");
    await page.getByRole("button", { name: /^Refresh:/ }).click();
    await expect(page.getByRole("button", { name: "Every 10 s" })).toHaveAttribute("aria-pressed", "true");
  });
  test("has one time range, in the top bar, and Simple and Advanced beside it", async ({ page }, testInfo) => {
    const phone = testInfo.project.name === "phone";
    await answerApi(page, { look: "console" });

    for (const path of ["/logs", "/traces", "/metrics"]) {
      await page.goto(path);
      await expect(page.getByRole("button", { name: /^(Simple|Advanced) view$/ })).toBeVisible();
      await expect(page.getByRole("group", { name: "View" })).toHaveCount(0);
      await expect(page.getByRole("group", { name: "Time range" })).toHaveCount(0);
      await expect(page.getByRole("banner").getByRole("button", { name: /^(Simple|Advanced) view$/ })).toHaveCount(
        phone ? 0 : 1,
      );
    }

    // On a phone the top bar keeps the range in short, and the Log Explorer's summary leaves it out.
    await page.goto("/logs");
    await expect(page.getByRole("button", { name: /^Time range:/ })).toContainText(phone ? "24h" : "Last 24 hours");
    await expect(page.locator(".filter-summary-chip", { hasText: "24h" })).toHaveCount(0);

    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });

  test("reads the alerts of the top bar's range, in whole days", async ({ page }) => {
    await answerApi(page, { look: "console" });
    const asked = page.waitForRequest((request) => request.url().includes("/api/v1/alerts/summary?"));
    await page.goto("/alerts?range=7d");

    expect(new URL((await asked).url()).searchParams.get("days")).toBe("7");
    await expect(page.getByRole("group", { name: "Period" })).toHaveCount(0);

    await page.getByRole("region", { name: "Alert list" }).getByRole("button", { name: /^Open/ }).click();
    await expect(page).toHaveURL(/range=7d/);
    await expect(page).toHaveURL(/state=open/);
  });

  test("offers to reload when a newer version runs, with what is new (VEY-82)", async ({ page }) => {
    await answerApi(page, {
      look: "console",
      builds: [
        { version: "0.4.1", build: "aaaaaaaaaaaa" },
        { version: "0.4.2", build: "bbbbbbbbbbbb" },
      ],
    });
    await page.goto("/logs");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();

    // The page asks again when it is shown again, as after a deploy while it sat in another tab.
    await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange")));
    const notice = page.getByRole("status", { name: "New version" });
    await expect(notice).toContainText("Veyra 0.4.2 is ready.");
    await expect(notice.getByRole("button", { name: "Reload" })).toBeVisible();

    await notice.getByRole("button", { name: "What's new" }).click();
    const notes = page.getByRole("dialog", { name: "What's new in Veyra" });
    await expect(notes).toContainText("A fix that came with 0.4.2.");
    await page.keyboard.press("Escape");
    await expect(notes).toHaveCount(0);

    await notice.getByRole("button", { name: "Later" }).click();
    await expect(notice).toHaveCount(0);
    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });
});
