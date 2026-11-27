import { expect, test, type Page } from "@playwright/test";

import { openIntegrity, openSevereError } from "../src/alerts/testAlerts";
import { postgresMetrics } from "../src/metrics/testMetrics";
import { answerApi } from "./api";

/*
 * Every tile explains itself with a question mark (VEY-60). On each screen, at every size and in both themes, each
 * question mark in view opens its explanation inside the window, readable, without the page scrolling sideways.
 */

const sqlServer = "/metrics/databases/microsoft.sql_server/sql-prod-01";

/** The screens, each by a name and its address. */
const screens: [string, string][] = [
  ["home", "/"],
  ["alerts", "/alerts"],
  ["alert details", `/alerts/${openIntegrity.id}`],
  ["severe error alert", `/alerts/${openSevereError.id}`],
  ["logs", "/logs"],
  ["traces", "/traces"],
  ["metrics", "/metrics"],
  ["host", "/metrics/hosts/web-01"],
  ["service", "/metrics/services/payments-gateway?environment=production"],
  ...["live", "queries", "waits", "blocking", "databases", "jobs", "events", "server"].map((tab): [string, string] => [
    `SQL Server ${tab}`,
    `${sqlServer}?tab=${tab}`,
  ]),
  ["PostgreSQL", "/metrics/databases/postgresql/pg-main"],
  ["keys", "/keys"],
  ["users", "/users"],
  ["notifications", "/notifications"],
  ["integrations", "/integrations"],
  ["integration", "/integrations/sqlserver/s1"],
  ["Database monitoring", "/settings?section=Databases"],
  ["agents", "/settings?section=Agents"],
  ["account", "/account"],
];

/** Whether the page, or any screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(
    () =>
      document.documentElement.scrollWidth > window.innerWidth ||
      [...document.querySelectorAll("main")].some((main) => main.scrollWidth > main.clientWidth + 1),
  );
}

for (const scheme of ["light", "dark"] as const) {
  for (const [name, path] of screens) {
    test(`explains every tile of ${name} in the ${scheme} theme`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await answerApi(page, {
        look: "forecast",
        alert: path.includes(openSevereError.id) ? openSevereError : openIntegrity,
      });
      await page.route("**/api/v1/metrics/databases/postgresql/pg-main**", (route) =>
        route.fulfill({ json: postgresMetrics }),
      );
      await page.goto(path);
      await page.locator("main").first().waitFor();
      await page.waitForLoadState("networkidle");
      expect(await scrollsSideways(page)).toBe(false);

      const buttons = page.getByRole("button", { name: /^What .* means$/ });
      // Screens draw their tiles once their data has loaded.
      await buttons.first().waitFor({ state: "attached" });
      let opened = 0;

      for (const button of await buttons.all()) {
        if (!(await button.isVisible())) {
          continue;
        }

        await button.scrollIntoViewIfNeeded();
        await button.click();
        const tip = page.getByRole("tooltip");
        await expect(tip).toBeVisible();

        const box = await tip.boundingBox();
        const viewport = page.viewportSize();
        expect(box?.x ?? -1).toBeGreaterThanOrEqual(0);
        expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(viewport?.width ?? 0);
        expect(box?.y ?? -1).toBeGreaterThanOrEqual(0);
        expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(viewport?.height ?? 0);

        const [colour, background] = await tip.evaluate((element) => {
          const style = getComputedStyle(element);

          return [style.color, style.backgroundColor];
        });
        expect(colour).not.toBe(background);

        await page.keyboard.press("Escape");
        await expect(tip).toBeHidden();
        opened += 1;
      }

      expect(opened, `no question mark in view on ${name}`).toBeGreaterThan(0);
      expect(await scrollsSideways(page)).toBe(false);
    });
  }
}
