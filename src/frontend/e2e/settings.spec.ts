import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page is wider than the window, the usual sign of a layout that breaks on small screens. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
}

test.describe("Settings", () => {
  test("changes a rule's threshold, reviews it, and saves it at every size", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings?section=Rules");

    const caution = page.getByRole("textbox", { name: "Error rate, Caution from" });
    await expect(caution).toHaveValue("1");
    expect(await scrollsSideways(page)).toBe(false);

    await caution.fill("2");
    const bar = page.getByRole("region", { name: "Unsaved changes" });
    await expect(bar).toContainText("Error rate, Caution from: 1 % to 2 %");
    await bar.getByRole("button", { name: "Review and save" }).click();

    const dialog = page.getByRole("dialog", { name: "Review 1 change" });
    await expect(dialog).toContainText("1 % → 2 %");
    expect(await scrollsSideways(page)).toBe(false);
    await dialog.getByRole("button", { name: "Save 1 change" }).click();

    await expect(page.getByRole("status")).toHaveText("Saved. Veyra uses the new values now.");
    await expect(bar).toBeHidden();
  });

  test("lays out the sections beside the settings, or as a list on a phone", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto("/settings");

    const sections = page.getByRole("navigation", { name: "Settings sections" });
    await expect(sections.getByRole("link", { name: /Alert rules/ })).toBeVisible();

    if (testInfo.project.name === "phone") {
      // A list first; a section opens on its own, with a way back.
      await expect(page.getByRole("textbox", { name: "Logs, Keep" })).toBeHidden();
      await sections.getByRole("link", { name: /Alert rules/ }).click();
      await expect(page.getByRole("textbox", { name: "Error rate, Caution from" })).toBeVisible();
      await expect(sections).toBeHidden();
      await page.getByRole("link", { name: "‹ All settings" }).click();
      await expect(sections).toBeVisible();
    } else {
      await expect(page.getByRole("textbox", { name: "Logs, Keep" })).toBeVisible();
      const list = await sections.boundingBox();
      const card = await page.getByRole("region", { name: "Logs" }).boundingBox();
      expect((card?.x ?? 0) > (list?.x ?? 0) + (list?.width ?? 0) - 1).toBe(true);
    }

    expect(await scrollsSideways(page)).toBe(false);
  });

  test("finds a setting with Ctrl+K and the keyboard alone", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "Phones have no keyboard shortcuts.");
    await answerApi(page);
    await page.goto("/settings");
    await expect(page.getByRole("textbox", { name: "Logs, Keep" })).toBeVisible();

    await page.keyboard.press("Control+k");
    await page.keyboard.type("smtp server");

    await expect(page.getByRole("searchbox", { name: "Find a setting" })).toBeFocused();
    await expect(page.getByRole("textbox", { name: "Email server, Server" })).toHaveValue("smtp.example.com");
  });

  test("starts Simple and shows every setting in Advanced, remembering the choice", async ({ page }) => {
    await answerApi(page, { view: "simple" });
    await page.goto("/settings?section=Rules");

    const interval = page.getByRole("textbox", { name: /Check the rules every/ });
    await expect(page.getByRole("textbox", { name: "Error rate, Caution from" })).toBeVisible();
    await expect(interval).toBeHidden();

    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Advanced" }).click();
    await expect(interval).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await page.reload();
    await expect(interval).toBeVisible();
  });

  test("opens the history and closes it with Escape", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings");

    await page.getByRole("button", { name: "History" }).click();
    const history = page.getByRole("complementary", { name: "Settings history" });
    await expect(history).toContainText("3 s → 2 s");
    await expect(history).toContainText("SQL Server · sql-prod-01");
    await expect(history).toContainText("Changed port, password");
    expect(await scrollsSideways(page)).toBe(false);

    await page.keyboard.press("Escape");
    await expect(history).toBeHidden();
  });

  test("is reached from the sidebar's admin group", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "The phone opens the sidebar from the top bar.");
    await answerApi(page);
    await page.goto("/");

    await page.getByRole("link", { name: "Settings" }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Settings" })).toBeVisible();
  });

  test("reads well in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page);
    await page.goto("/settings?section=Rules");

    const card = page.getByRole("region", { name: "Error rate" });
    await expect(card).toBeVisible();
    const colors = await card.evaluate((element) => {
      const style = getComputedStyle(element);

      return { background: style.backgroundColor, color: style.color };
    });
    expect(colors.background).not.toBe(colors.color);
  });

  test("clears all telemetry once the site's name is typed, at every size", async ({ page }) => {
    await answerApi(page);
    let cleared: unknown = null;
    await page.route("**/api/v1/settings/clear-telemetry", async (route) => {
      cleared = route.request().postDataJSON();
      await route.fulfill({ status: 204 });
    });
    await page.goto("/settings?section=Data");

    const panel = page.getByRole("region", { name: "Clear all telemetry" });
    const button = panel.getByRole("button", { name: "Clear all telemetry" });
    await expect(button).toBeDisabled();
    expect(await scrollsSideways(page)).toBe(false);

    await panel.getByRole("textbox").fill("localhost");
    await button.click();

    await expect(panel.getByRole("status")).toContainText("Cleared.");
    expect(cleared).toEqual({ confirm: "localhost" });
  });
});
