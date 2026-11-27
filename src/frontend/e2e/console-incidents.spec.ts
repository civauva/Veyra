import { expect, test } from "@playwright/test";

import { answerApi } from "./api";

test.describe("Console Incidents (VEY-76)", () => {
  test("lists the incidents, open first, and opens one", async ({ page }, testInfo) => {
    await answerApi(page, { look: "console" });
    await page.goto("/incidents");

    const list = page.getByRole("region", { name: "Incidents", exact: true });
    await expect(list).toContainText("INC-42");
    await expect(list).toContainText("INC-39");
    // On a phone the menu waits in its drawer.
    if (testInfo.project.name !== "phone") {
      await expect(
        page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Incidents" }),
      ).toHaveAttribute("aria-current", "page");
    }

    await list.getByRole("link", { name: "CustomerSync error rate 8.4%" }).click();
    await expect(page).toHaveURL(/\/incidents\/inc-42/);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("CustomerSync error rate 8.4%");
  });

  test("shows every part of the reference, without scrolling sideways", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/incidents/inc-42");

    for (const panel of ["Execution path", "Incident timeline", "Correlated logs", "Investigation context"]) {
      await expect(page.getByRole("region", { name: panel })).toBeVisible();
    }

    await expect(page.getByRole("region", { name: "Execution path" })).toContainText("PostgreSQL write");
    await expect(page.getByRole("region", { name: "Execution path" })).toContainText("Npgsql.NpgsqlException");
    await expect(page.getByRole("region", { name: "Incident timeline" })).toContainText("Notification sent");
    await expect(page.getByRole("region", { name: "Incident timeline" })).toContainText("First error");
    await expect(page.getByRole("region", { name: "Error rate, CustomerSync" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Investigation context" })).toContainText("docker-02");
    await expect(page.getByRole("link", { name: "View runbook →" })).toHaveAttribute(
      "href",
      "https://wiki.example.com/runbooks/error-rate",
    );

    const sideways = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(sideways).toBe(false);
  });

  test("acknowledges the incident and shows who did", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/incidents/inc-42");

    const saved = page.waitForRequest(
      (request) => request.url().endsWith("/incidents/inc-42/acknowledge") && request.method() === "POST",
    );
    await page.getByRole("button", { name: "Acknowledge" }).click();
    await saved;
    await expect(page.getByRole("heading", { level: 1 })).toContainText("acknowledged");
    await expect(page.getByText("acknowledged by Lena Admin")).toBeVisible();
  });

  test("opens the correlated logs in the Log explorer with the server's query", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/incidents/inc-42");

    await page.getByRole("link", { name: "Open in Logs →" }).click();
    await expect(page).toHaveURL(/\/logs\?q=service%3ACustomerSync/);
  });
});
