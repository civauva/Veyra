import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page, or the screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const screen = document.querySelector(".notifications");

    return (
      document.documentElement.scrollWidth > window.innerWidth ||
      (screen !== null && screen.scrollWidth > screen.clientWidth)
    );
  });
}

test.describe("Notifications", () => {
  test("lays the channels out for the screen size", async ({ page }, testInfo) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/notifications");

    const ops = page.getByRole("article", { name: "#ops-alerts" });
    const bot = page.getByRole("article", { name: "Incident bot" });
    await expect(ops).toBeVisible();
    await expect(bot.getByText(/Last try failed at 07:04/)).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    const opsBox = await ops.boundingBox();
    const botBox = await bot.boundingBox();

    if (testInfo.project.name === "phone") {
      // One card per row.
      expect(botBox?.y ?? 0).toBeGreaterThan((opsBox?.y ?? 0) + (opsBox?.height ?? 0) - 1);
    } else {
      // Two cards per row.
      expect(botBox?.x ?? 0).toBeGreaterThan((opsBox?.x ?? 0) + (opsBox?.width ?? 0));
    }
  });

  test("opens the side panel, sends a test, and closes with Escape", async ({ page }, testInfo) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/notifications");

    await page.getByRole("button", { name: "+ Add channel" }).click();
    const panel = page.getByRole("dialog", { name: "Add a channel" });
    await expect(panel).toBeVisible();
    await expect(panel.getByLabel("Name")).toBeFocused();

    await panel.getByLabel("Name").fill("#ops-alerts");
    await panel.getByLabel("URL").fill("https://hooks.slack.com/services/T0A1/B0C2/x9Y");
    await panel.getByRole("button", { name: "Send test" }).click();
    await expect(panel.getByRole("status")).toHaveText("Test delivered (204).");

    // The panel's buttons stay reachable at every size.
    await expect(panel.getByRole("button", { name: "Save channel" })).toBeInViewport();

    if (testInfo.project.name !== "phone") {
      await page.keyboard.press("Escape");
      await expect(panel).toBeHidden();
    }
  });

  test("keeps each switch's knob inside its track", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/notifications");
    await expect(page.getByRole("switch").first()).toBeVisible();

    // Notifications and Users once each styled .switch; together they pushed a switched-on knob off the track.
    const knobs = await page.getByRole("switch").evaluateAll((switches) =>
      switches.map((control) => {
        const knob = getComputedStyle(control, "::after");
        const shift = new DOMMatrix(knob.transform === "none" ? undefined : knob.transform).m41;
        const right = parseFloat(knob.left) + shift + parseFloat(knob.width);

        return { on: control.getAttribute("aria-checked"), right, track: control.clientWidth };
      }),
    );

    expect(knobs.some((knob) => knob.on === "true")).toBe(true);
    expect(knobs.filter((knob) => knob.right > knob.track)).toEqual([]);
  });

  test("keeps the channel cards readable in the dark theme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "dark" });
    await answerApi(page, { look: "forecast" });
    await page.goto("/notifications");

    const card = page.getByRole("article", { name: "#ops-alerts" });
    await expect(card).toBeVisible();

    const background = await card.evaluate((element) => getComputedStyle(element).backgroundColor);
    const colour = await card
      .getByRole("heading", { name: "#ops-alerts" })
      .evaluate((element) => getComputedStyle(element).color);

    // The dark panel, a little translucent so the weather band shows through on work screens (VEY-23).
    expect(background).toBe("rgba(16, 24, 51, 0.78)");
    expect(colour).toBe("rgb(236, 239, 248)");
  });
});
