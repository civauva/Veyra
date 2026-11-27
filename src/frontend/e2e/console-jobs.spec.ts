import { expect, test, type Page } from "@playwright/test";

import { answerApi } from "./api";

/** Whether the page, or any screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(
    () =>
      document.documentElement.scrollWidth > window.innerWidth ||
      [...document.querySelectorAll("main")].some((main) => main.scrollWidth > main.clientWidth + 1),
  );
}

test.describe("Console Jobs & pipelines (ADR 0027)", () => {
  test("lists the jobs with the server's figures, failing first, without scrolling sideways", async ({
    page,
  }, testInfo) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs");

    const figures = page.getByRole("region", { name: "Figures" });
    await expect(figures).toContainText("Jobs");
    await expect(figures).toContainText("121");
    await expect(figures).toContainText("97.5%");
    await expect(page.getByRole("heading", { level: 1 })).toContainText("3 failed runs");

    const list = page.getByRole("region", { name: "Jobs", exact: true });
    const rows = list.getByRole("row");
    await expect(rows.nth(1)).toContainText("CustomerSync");
    await expect(rows.nth(1)).toContainText("Failed");
    await expect(rows.nth(2)).toContainText("Nightly backup");
    await expect(rows.nth(3)).toContainText("Running");

    if (testInfo.project.name !== "phone") {
      await expect(
        page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Jobs & pipelines" }),
      ).toHaveAttribute("aria-current", "page");
      await expect(list.getByRole("link", { name: "sql-prod-01 server" })).toHaveAttribute(
        "href",
        "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=jobs",
      );
      await expect(list.getByRole("img", { name: /Latest 20 runs, oldest first: 1 failed/ })).toBeVisible();
    }

    expect(await scrollsSideways(page)).toBe(false);
  });

  test("sends the search, the status, and the source to the server", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs");
    const list = page.getByRole("region", { name: "Jobs", exact: true });
    await expect(list).toContainText("OrderExport");

    const searched = page.waitForRequest(
      (request) => request.url().includes("/api/v1/jobs?") && new URL(request.url()).searchParams.get("q") === "sync",
    );
    await page.getByRole("searchbox", { name: "Find a job" }).fill("sync");
    await searched;
    await expect(list.getByRole("row")).toHaveCount(2);
    await expect(page).toHaveURL(/[?&]q=sync/);

    const failing = page.waitForRequest((request) => {
      const params = new URL(request.url()).searchParams;

      return (
        request.url().includes("/api/v1/jobs?") && params.get("status") === "failing" && params.get("q") === "sync"
      );
    });
    await page.getByRole("group", { name: "Status" }).getByRole("button", { name: "Failing" }).click();
    await failing;

    await page.getByRole("searchbox", { name: "Find a job" }).fill("");
    const agent = page.waitForRequest((request) => {
      const params = new URL(request.url()).searchParams;

      return request.url().includes("/api/v1/jobs?") && params.get("source") === "sqlagent";
    });
    await page.getByRole("group", { name: "Status" }).getByRole("button", { name: "All" }).click();
    await page.getByRole("group", { name: "Source" }).getByRole("button", { name: "SQL Agent" }).click();
    await agent;
    await expect(list.getByRole("row")).toHaveCount(2);
    await expect(list).toContainText("Nightly backup");
    await expect(
      page.getByRole("group", { name: "Source" }).getByRole("button", { name: "SQL Agent" }),
    ).toHaveAttribute("aria-pressed", "true");
  });

  test("opens a job with its runs, the failed run's steps, its trace, and its logs", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs");

    await page
      .getByRole("region", { name: "Jobs", exact: true })
      .getByRole("link", { name: "CustomerSync", exact: true })
      .click();
    await expect(page).toHaveURL(/\/jobs\/job-customersync/);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Last run failed");
    await expect(page.getByRole("region", { name: "Figures" })).toContainText("96.8%");
    await expect(page.getByRole("img", { name: /Durations of 96 runs/ })).toBeVisible();

    const runs = page.getByRole("region", { name: "Runs", exact: true });
    await expect(runs.getByRole("row")).toHaveCount(9);
    await expect(runs.getByRole("row").nth(1).getByRole("button")).toHaveAttribute("aria-pressed", "true");

    const run = page.locator("#jb-run");
    const steps = run.getByRole("list", { name: "Steps" });
    await expect(steps.getByRole("listitem")).toHaveCount(4);
    await expect(steps).toContainText("PostgreSQL write");
    await expect(steps).toContainText("timeout after 30 s");
    await expect(steps).toContainText("did not run");
    await expect(run).toContainText("31,200 of 50,000");
    await expect(run).toContainText("Npgsql.NpgsqlException");
    await expect(run.getByRole("link", { name: "Open the trace →" })).toHaveAttribute(
      "href",
      "/traces/4bf92f3577b34da6a3ce929d0e0e4736",
    );
    const logs = await run.getByRole("link", { name: "Logs around the run →" }).getAttribute("href");
    const params = new URLSearchParams(logs?.split("?")[1]);
    expect(params.get("q")).toBe("service:CustomerSync");
    expect(params.get("from")).toBe("2026-11-26T14:44:00.000Z");

    expect(await scrollsSideways(page)).toBe(false);
  });

  test("picks an older run, whose trace has expired", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs/job-customersync");

    const runs = page.getByRole("region", { name: "Runs", exact: true });
    await runs.getByRole("button").last().click();
    await expect(runs.getByRole("button").last()).toHaveAttribute("aria-pressed", "true");
    await expect(page).toHaveURL(/[?&]run=run-88/);

    const run = page.locator("#jb-run");
    await expect(run).toContainText("Trace expired");
    await expect(run.getByRole("link", { name: "Open the trace →" })).toHaveCount(0);
    await expect(run.getByRole("list", { name: "Steps" })).toContainText("Publish events");
  });

  test("shows a SQL Server Agent job with its steps and its server, without a trace", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs/job-nightly-backup");

    await expect(page.getByRole("heading", { level: 1 })).toContainText("Nightly backup");
    await expect(page.getByRole("link", { name: "Open the server's jobs →" })).toHaveAttribute(
      "href",
      "/metrics/databases/microsoft.sql_server/sql-prod-01?tab=jobs",
    );
    const run = page.locator("#jb-run");
    await expect(run.getByRole("list", { name: "Steps" })).toContainText("Back up databases");
    await expect(run.getByRole("link", { name: "Open the trace →" })).toHaveCount(0);
    await expect(run.getByRole("link", { name: "Logs around the run →" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Open in Logs →" })).toHaveCount(0);
  });

  test("says when the server knows no such job", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs/job-gone");

    await expect(page.getByText("Veyra knows no such job.")).toBeVisible();
  });

  test("opens a job from the Overview's pipeline executions", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/");

    await page
      .getByRole("region", { name: "Pipeline executions" })
      .getByRole("link", { name: "CustomerSync", exact: true })
      .click();
    await expect(page).toHaveURL(/\/jobs\/job-customersync/);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("CustomerSync");
  });
});

test.describe("Jobs in the Forecast look (ADR 0027)", () => {
  test("lists the jobs plainly and opens one with its runs", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/jobs");

    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Jobs and pipelines");
    const searched = page.waitForRequest(
      (request) => request.url().includes("/api/v1/jobs?") && new URL(request.url()).searchParams.get("q") === "backup",
    );
    await page.getByRole("searchbox", { name: "Find a job" }).fill("backup");
    await searched;
    await page.getByRole("link", { name: "Nightly backup" }).click();

    await expect(page).toHaveURL(/\/jobs\/job-nightly-backup/);
    await expect(
      page.getByText("Back up databases succeeded → Verify backups succeeded", { exact: false }),
    ).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("lists the failing jobs from the Failed figure (VEY-82)", async ({ page }) => {
    await answerApi(page, { look: "console" });
    await page.goto("/jobs");
    const figures = page.getByRole("region", { name: "Figures" });

    const asked = page.waitForRequest(
      (request) => request.url().includes("/api/v1/jobs?") && request.url().includes("status=failing"),
    );
    await figures.getByRole("button", { name: "Failed", exact: true }).click();
    await asked;
    await expect(page).toHaveURL(/status=failing/);
  });
});
