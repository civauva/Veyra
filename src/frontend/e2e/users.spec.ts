import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
}

test.describe("Users", () => {
  test("lays the badges out and opens a person beside them or as a sheet", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto("/users");

    const badges = page.getByRole("list", { name: "People" }).getByRole("listitem");
    await expect(badges).toHaveCount(8);
    expect(await scrollsSideways(page)).toBe(false);

    await page.getByRole("button", { name: /Tom Okafor/ }).click();
    const panel = page.getByRole("region", { name: "Tom Okafor" });
    await expect(panel).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    const grid = await page.getByRole("list", { name: "People" }).boundingBox();
    const box = await panel.boundingBox();

    if (testInfo.project.name === "desktop") {
      // Beside the grid.
      expect(box?.x ?? 0).toBeGreaterThan((grid?.x ?? 0) + (grid?.width ?? 0) - 1);
    } else {
      // Over the grid, as a sheet, closed with its button.
      expect(await panel.evaluate((element) => getComputedStyle(element).position)).toBe("fixed");
      await panel.getByRole("button", { name: "Close" }).click();
      await expect(panel).toBeHidden();
    }
  });

  test("opens a person with the keyboard", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === "phone", "Phones have no keyboard to tab with.");
    await answerApi(page);
    await page.goto("/users");

    const badge = page.getByRole("button", { name: /Tom Okafor/ });
    await badge.focus();
    await page.keyboard.press("Enter");
    const panel = page.getByRole("region", { name: "Tom Okafor" });
    await panel.getByRole("tab", { name: /Sign-ins/ }).click();

    await expect(panel.getByText("Address not seen before")).toBeVisible();
  });

  test("adds a user from the side panel", async ({ page }) => {
    await answerApi(page);
    await page.goto("/users");

    await page.getByRole("button", { name: "+ Add user" }).click();
    const drawer = page.getByRole("dialog", { name: "Add a user" });

    await expect(drawer.getByRole("group", { name: "First password" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.keyboard.press("Escape");
    await expect(drawer).toBeHidden();
  });
});

test.describe("Account", () => {
  test("shows the account without sideways scrolling, in the dark theme too", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page);
    await page.goto("/account");

    await expect(page.getByRole("heading", { level: 1, name: "Your account" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Two-factor sign-in" })).toContainText("9 of 10 recovery codes");
    expect(await scrollsSideways(page)).toBe(false);

    const panel = page.getByRole("region", { name: "Recent sign-ins" });
    expect(await panel.evaluate((element) => getComputedStyle(element).backgroundColor)).toBe("rgba(16, 24, 51, 0.78)");
  });

  test("is reached from the avatar in the top bar", async ({ page }, testInfo) => {
    await answerApi(page);
    await page.goto("/");

    if (testInfo.project.name !== "desktop") {
      await page.getByRole("button", { name: "Menu" }).click();
    }

    await page.getByRole("link", { name: "Your account" }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Your account" })).toBeVisible();
  });
});

test.describe("Signing in with a code", () => {
  test("asks for the code after the password", async ({ page }, testInfo) => {
    await answerApi(page, { signIn: "code" });
    await page.goto("/");

    await page.getByLabel("Email").fill("ana@example.com");
    await page.getByLabel("Password").fill("correct horse battery");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByRole("heading", { name: "Enter your code" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.getByLabel("Code from the app").fill("482093");
    await page.getByRole("button", { name: "Sign in" }).click();

    // Below desktop width the navigation waits in the closed menu.
    await expect(
      testInfo.project.name !== "desktop"
        ? page.getByRole("button", { name: "Menu" })
        : page.getByRole("navigation", { name: "Main" }),
    ).toBeVisible();
  });
});
