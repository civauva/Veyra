import { expect, test, type Page } from "@playwright/test";

import { openWarning } from "../src/alerts/testAlerts";
import { checkoutId } from "../src/traces/testTraces";
import { answerApi } from "./api";

/** Whether the page or a screen inside it scrolls sideways, which no screen may at any size. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(
    () =>
      document.documentElement.scrollWidth > window.innerWidth ||
      [...document.querySelectorAll("main")].some((main) => main.scrollWidth > main.clientWidth + 1),
  );
}

test.describe("Console traces, a trace, metrics, an alert, and settings (ADR 0029)", () => {
  test("lists traces worst first with the server's figures, and a service filters them", async ({ page }) => {
    // Console is the default look: the session names no look of the person's own.
    await answerApi(page);
    const worst = page.waitForRequest(
      (request) =>
        new URL(request.url()).pathname === "/api/v1/traces" &&
        new URL(request.url()).searchParams.get("order") === "worst",
    );
    await page.goto("/traces");
    await worst;

    const figures = page.getByRole("region", { name: "Figures" });
    await expect(figures).toContainText("18,402");
    await expect(figures).toContainText("↑ 6% vs the period before");
    await expect(figures).toContainText("p95 214 ms over the range");
    await expect(page.getByRole("heading", { level: 1 })).toContainText("3 failed · 920 slow");

    const filtered = page.waitForRequest(
      (request) =>
        new URL(request.url()).pathname === "/api/v1/traces" &&
        new URL(request.url()).searchParams.get("service") === "checkout-web",
    );
    await page.getByRole("region", { name: "Slowest services" }).getByRole("button", { name: "checkout-web" }).click();
    await filtered;
    await expect(page).toHaveURL(/service=checkout-web/);
    await expect(page.getByRole("combobox", { name: "Service", exact: true })).toHaveValue("checkout-web");

    await expect(page.getByRole("table", { name: "Traces" }).getByText("Failed")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("opens a trace with its figures, waterfall, and the span that failed", async ({ page }) => {
    await answerApi(page);
    await page.goto(`/traces/${checkoutId}`);

    await expect(page.getByRole("heading", { level: 1 })).toContainText("POST /api/checkout");
    await expect(page.getByRole("region", { name: "Figures" })).toContainText("System.TimeoutException in psp-client");
    await expect(page.getByRole("list", { name: "Spans" }).getByRole("listitem")).toHaveCount(5);
    await expect(page.getByRole("region", { name: "POST /authorize" })).toContainText(
      "The PSP did not answer within 1.5 s",
    );

    const errorsOnly = page.waitForRequest((request) => request.url().includes("errorsOnly=true"));
    await page.getByRole("button", { name: "Errors only" }).click();
    await errorsOnly;
    await expect(page.getByText("3 of 5 spans")).toBeVisible();

    await page.getByRole("tab", { name: /Logs of this trace/ }).click();
    await expect(page.getByText("Authorization failed: TimeoutException after 1500 ms")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("charts a metric for one service and host, with each line's figures from the server", async ({ page }) => {
    await answerApi(page);
    await page.goto("/metrics?tab=all&metric=http.server.request.duration&show=p95&split=service.name");

    const series = page.getByRole("table", { name: "Series" });
    await expect(series.getByRole("row", { name: /checkout-web/ })).toContainText("742 ms");
    await expect(series.getByRole("row", { name: /checkout-web/ })).toContainText("512 ms");

    const sent = page.waitForRequest((request) => {
      const url = new URL(request.url());

      return (
        url.pathname === "/api/v1/metrics/chart" &&
        url.searchParams.get("service") === "checkout-web" &&
        url.searchParams.get("host") === "web-01"
      );
    });
    await page.getByRole("combobox", { name: "Service", exact: true }).fill("checkout-web");
    await page.getByRole("combobox", { name: "Service", exact: true }).press("Enter");
    await page.getByRole("combobox", { name: "Host", exact: true }).fill("web-01");
    await page.getByRole("combobox", { name: "Host", exact: true }).press("Enter");
    await sent;
    await expect(page).toHaveURL(/service=checkout-web/);
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("shows an alert with the logs around it and the blocks it touches", async ({ page }) => {
    await answerApi(page);
    await page.goto(`/alerts/${openWarning.id}`);

    await expect(page.getByRole("heading", { level: 1 })).toContainText("Warning · open");
    const logs = page.getByRole("region", { name: "Logs around this" });
    await expect(logs).toContainText("Payment declined by PSP: upstream timeout after 1800 ms");
    await expect(logs).toContainText("2 of 31");

    const affected = page.getByRole("region", { name: "Affected" });
    await expect(affected.getByRole("row", { name: /payments-gateway/ })).toContainText("Warning");
    await expect(affected.getByRole("row", { name: /orders-db/ })).toContainText("Caution");
    await expect(page.getByRole("region", { name: "How to fix" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Rule settings" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("offers the looks as cards and collects edits in a save bar to review", async ({ page }) => {
    await answerApi(page);
    await page.goto("/settings?section=Appearance");

    const looks = page.getByRole("radiogroup", { name: "Look" });
    await expect(looks.getByRole("radio")).toHaveCount(2);
    await expect(looks.getByRole("radio", { name: /Console/ })).toBeChecked();

    const everyone = page.getByRole("region", { name: "Look for everyone" });
    await everyone.getByRole("combobox", { name: /Default look/ }).selectOption("console");

    const bar = page.getByRole("region", { name: "Unsaved changes" });
    await expect(bar).toContainText("1 change");
    await bar.getByRole("button", { name: "Review and save" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await page.getByRole("dialog").getByRole("button", { name: "Back" }).click();

    await bar.getByRole("button", { name: "Discard" }).click();
    await expect(bar).toBeHidden();
    expect(await scrollsSideways(page)).toBe(false);
  });
});
