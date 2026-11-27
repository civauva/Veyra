import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

test.describe("Console Log explorer (VEY-74)", () => {
  test("shows every part of the reference, with the time left to the top bar", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs");

    await expect(page.getByRole("heading", { level: 1, name: "Logs" })).toBeVisible();
    await expect(page.getByRole("textbox", { name: "Query" })).toBeVisible();

    for (const region of ["Log volume", "By level", "Events", "Event"]) {
      await expect(page.getByRole("region", { name: region, exact: true })).toBeVisible();
    }

    const filters = page.getByRole("group", { name: "Filters" });
    for (const chip of ["Level", "Service", "Environment", "Host", "Trace"]) {
      await expect(filters.getByRole("button", { name: new RegExp(`^${chip}:`) })).toBeVisible();
    }
    await expect(filters.getByRole("button", { name: /^Time/ })).toHaveCount(0);

    const inspector = page.getByRole("region", { name: "Event", exact: true });
    await expect(inspector).toContainText("Exception");
    await expect(inspector).toContainText("System.TimeoutException");
    await expect(inspector).toContainText("Correlation");
    await expect(page.getByRole("region", { name: "Events" })).toContainText("web-02");
    await expect(page.getByRole("region", { name: "By level" })).toContainText("2,610");

    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });

  test("runs a query on the server, shows its terms as chips, and removes one", async ({ page }) => {
    const searches = await answerApi(page, { look: "console" });
    await page.goto("/logs");

    const bar = page.getByRole("textbox", { name: "Query" });
    await bar.fill("service:payments-gateway AND host:web-02");
    await page.getByRole("button", { name: /Run/ }).click();

    await expect(page).toHaveURL(/q=service/);
    await expect
      .poll(() => searches.some((request) => request.url().includes("q=service%3Apayments-gateway")))
      .toBe(true);
    const filters = page.getByRole("group", { name: "Filters" });
    await expect(filters.getByRole("button", { name: /^Service: payments-gateway/ })).toBeVisible();
    await expect(filters.getByRole("button", { name: /^Host: web-02/ })).toBeVisible();

    await filters.getByRole("button", { name: "Remove the host filter" }).click();
    await expect(bar).toHaveValue("service:payments-gateway");
  });

  test("says where a query went wrong", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs?q=level%3E%3Dwarnig");

    await expect(page.getByRole("alert")).toContainText("At 8:");
    await expect(page.getByRole("textbox", { name: "Query" })).toHaveAttribute("aria-invalid", "true");
  });

  test("filters by a level from By level, and opens a saved view", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs");

    await page
      .getByRole("region", { name: "By level" })
      .getByRole("button", { name: /^Warning/ })
      .click();
    await expect(page).toHaveURL(/q=level%3Awarning/);

    await page.getByRole("button", { name: /^View:/ }).click();
    await page
      .getByRole("dialog", { name: "Saved views" })
      .getByRole("button", { name: /^Orders/ })
      .click();
    await expect(page).toHaveURL(/q=service%3Aorders-api/);
    await expect(page.getByRole("button", { name: /^View: Orders/ })).toBeVisible();
  });

  test("saves the query as a view", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs?q=host%3Aweb-02");

    const saved = page.waitForRequest(
      (request) => request.url().endsWith("/api/v1/views") && request.method() === "POST",
    );
    await page.getByRole("button", { name: "Save view" }).click();
    const dialog = page.getByRole("dialog", { name: "Save view" });
    await dialog.getByRole("textbox", { name: "Name" }).fill("Web two");
    await dialog.getByRole("checkbox", { name: "Share with everyone" }).check();
    await dialog.getByRole("button", { name: "Save" }).click();

    expect((await saved).postDataJSON()).toMatchObject({ name: "Web two", query: "host:web-02", shared: true });
  });

  test("steps through events and shows the inspector's tabs", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/logs");

    const inspector = page.getByRole("region", { name: "Event", exact: true });
    await expect(inspector).toContainText("1 of 3,540");
    await inspector.getByRole("button", { name: "Next event" }).click();
    await expect(inspector).toContainText("2 of 3,540");

    await inspector.getByRole("button", { name: "Previous event" }).click();
    await inspector.getByRole("tab", { name: /^Related/ }).click();
    await expect(inspector.getByRole("tabpanel")).toContainText("payments-gateway");
    await inspector.getByRole("tab", { name: "JSON" }).click();
    await expect(inspector.getByRole("tabpanel")).toContainText('"serviceName"');
  });
});
