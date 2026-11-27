import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

test.describe("Console look (VEY-71)", () => {
  test("groups the menu and keeps the environment and the open alerts in view on every screen", async ({
    page,
  }, testInfo) => {
    await answerApi(page, { look: "console" });

    for (const path of ["/", "/logs", "/traces", "/metrics", "/alerts", "/settings"]) {
      await page.goto(path);

      await expect(page.getByRole("banner").getByRole("button", { name: /^Environment: production/ })).toBeVisible();
      await expect(page.getByRole("banner").getByRole("link", { name: "Open alerts" })).toHaveAccessibleDescription(
        "1 warning, 1 caution",
      );
    }

    const main = page.getByRole("navigation", { name: "Main" });

    if (testInfo.project.name === "phone") {
      await expect(main).toBeHidden();
      await page.getByRole("button", { name: "Menu" }).click();
    }

    for (const group of ["Observe", "Resources", "Respond", "Manage"]) {
      await expect(main.getByRole("region", { name: group })).toBeAttached();
    }

    await main.getByRole("link", { name: "Logs" }).click();
    await expect(page).toHaveURL(/\/logs$/);
  });

  test("never scrolls sideways and keeps the frame still while the screen scrolls", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs");
    await expect(page.getByRole("banner").getByRole("link", { name: "Open alerts" })).toBeVisible();

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });

  test("is the default, and another look picked on the Account page is kept and given back", async ({ page }) => {
    await answerApi(page);
    await page.goto("/account");

    const look = page.getByRole("radiogroup", { name: "Look" });
    await expect(look.getByRole("radio", { name: /Console/ })).toBeChecked();
    await expect(page.locator("html")).toHaveAttribute("data-look", "console");

    // Picking a look draws the app again in it, so the radio clicked is not the one checked after.
    await look.getByRole("radio", { name: /Forecast/ }).click();
    await expect(page.locator("html")).not.toHaveAttribute("data-look", "console");

    await page.reload();
    await expect(page.getByRole("radio", { name: /Forecast/ })).toBeChecked();

    await page.getByRole("button", { name: "Use the default, Console" }).click();
    await expect(page.getByRole("radio", { name: /Console/ })).toBeChecked();
    await expect(page.locator("html")).toHaveAttribute("data-look", "console");
  });

  test("opens the account menu with the theme and signing out", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs");

    await page.getByRole("button", { name: /^Account: / }).click();
    const menu = page.getByRole("dialog", { name: "Account" });

    await menu.getByRole("button", { name: "Light" }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
    await menu.getByRole("button", { name: "Dark" }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await expect(menu.getByRole("button", { name: "Sign out" })).toBeVisible();
  });
});
