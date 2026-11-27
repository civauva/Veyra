import { expect, test, type Page } from "@playwright/test";

import { checkoutId } from "../src/traces/testTraces";
import { answerApi } from "./api";

/** Whether the page, or the screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page, selector: string): Promise<boolean> {
  return page.evaluate((area) => {
    const screen = document.querySelector(area);

    return (
      document.documentElement.scrollWidth > window.innerWidth ||
      (screen !== null && screen.scrollWidth > screen.clientWidth)
    );
  }, selector);
}

test.describe("Traces", () => {
  test("lists traces with the chart and opens one at every size", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/traces");

    await expect(page.getByRole("img", { name: /3 traces: 1 failed/ })).toBeVisible();
    const row = page.getByRole("row", { name: /POST \/api\/checkout/ });
    await expect(row).toContainText("psp-client: The PSP did not answer");
    expect(await scrollsSideways(page, ".traces-main")).toBe(false);

    await row.click();

    await expect(page.getByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toBeVisible();
    await expect(page.getByRole("status")).toContainText("Part of this trace has not arrived");
    expect(await scrollsSideways(page, ".trace-main")).toBe(false);
  });

  test("shows the cause beside the waterfall, or as a sheet on phones", async ({ page }, testInfo) => {
    await answerApi(page, { look: "forecast" });
    await page.goto(`/traces/${checkoutId}?at=2026-11-09T12:00:00.000Z`);

    const panel = page.getByRole("complementary", { name: "POST /authorize" });

    if (testInfo.project.name === "phone") {
      // On phones the waterfall comes first; the span opens as a sheet when tapped.
      await expect(panel).toHaveCount(0);
      await page.getByRole("button", { name: "POST /authorize" }).click();
    }

    await expect(panel).toBeVisible();
    await expect(panel.getByText("System.TimeoutException")).toBeVisible();
    await expect(panel.getByText("Authorization failed: TimeoutException after 1500 ms")).toBeVisible();

    const box = await panel.boundingBox();
    const viewport = page.viewportSize();

    if (testInfo.project.name === "phone") {
      expect(Math.round((box?.y ?? 0) + (box?.height ?? 0))).toBe(viewport?.height);
    } else {
      expect(Math.round((box?.x ?? 0) + (box?.width ?? 0))).toBe(viewport?.width);
    }
  });

  test("asks the server for the trace's records oldest first", async ({ page }) => {
    // The server orders the records (VEY-68), so the order must reach it.
    await answerApi(page, { look: "forecast" });
    const oldest = page.waitForRequest((request) => {
      const url = new URL(request.url());

      return (
        url.pathname === "/api/v1/logs" &&
        url.searchParams.get("traceId") === checkoutId &&
        url.searchParams.get("order") === "oldest"
      );
    });

    await page.goto(`/traces/${checkoutId}`);
    await oldest;
  });

  test("asks the server for the failed spans and for the chosen span's records", async ({ page }, testInfo) => {
    // The server filters the spans and the records (VEY-67), so each choice must reach it.
    await answerApi(page, { look: "forecast" });
    const asked = (path: string, name: string, value: string) =>
      page.waitForRequest((request) => {
        const url = new URL(request.url());

        return url.pathname === path && url.searchParams.get(name) === value;
      });
    // Beside the waterfall the trace opens on its cause, so that span's records are asked for at once; on a phone the
    // span opens when tapped.
    const spanLogs = asked("/api/v1/logs", "spanId", "0000000000000003");
    await page.goto(`/traces/${checkoutId}`);

    if (testInfo.project.name === "phone") {
      await page.getByRole("button", { name: "POST /authorize" }).click();
    }

    await spanLogs;

    // A phone has no room for the waterfall's toggles.
    if (testInfo.project.name === "phone") {
      return;
    }

    // On a tablet the span lies over the toggles; it is closed to reach them.
    if (testInfo.project.name === "tablet") {
      await page.getByRole("button", { name: "Close the span" }).click();
    }

    const errors = asked(`/api/v1/traces/${checkoutId}`, "errorsOnly", "true");
    await page.getByRole("button", { name: "Errors only" }).click();
    await errors;
    await expect(page.getByRole("button", { name: /SELECT payments.customer/ })).toHaveCount(0);
  });

  test("goes from a log record to its trace", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/logs");

    await page
      .getByRole("button", { name: /Payment declined by PSP/ })
      .first()
      .click();
    await expect(page.getByRole("link", { name: "Open trace" })).toHaveAttribute(
      "href",
      /\/traces\/5b8efff798038103d269b633813fc60c\?at=/,
    );
  });
});
