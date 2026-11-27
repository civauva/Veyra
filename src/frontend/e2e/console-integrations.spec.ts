import { expect, test, type Page } from "@playwright/test";

import { windowsAgent } from "../src/integrations/testIntegrations";
import { builtInAgent } from "../src/settings/testMonitoring";
import { answerApi } from "./api";

/** Whether the page, or any screen's own scrolling area, is wider than the window. */
async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(
    () =>
      document.documentElement.scrollWidth > window.innerWidth ||
      [...document.querySelectorAll("main")].some((main) => main.scrollWidth > main.clientWidth + 1),
  );
}

/** Answers the API in Console, with a Windows agent beside the built-in one. */
async function openConsole(page: Page): Promise<void> {
  await answerApi(page, { look: "console" });
  await page.route("**/api/v1/agents", (route) =>
    route.request().method() === "GET" ? route.fulfill({ json: [builtInAgent, windowsAgent] }) : route.fallback(),
  );
}

/** A probe takes two reads three seconds apart in the mock, so its answer comes after about six seconds. */
const probeWait = { timeout: 20_000 };

test.describe("Console Integrations (ADR 0028)", () => {
  test("lists the integrations with their state, version, and modules, without scrolling sideways", async ({
    page,
  }, testInfo) => {
    await openConsole(page);
    await page.goto("/integrations");

    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Integrations");
    const list = page.getByRole("region", { name: "SQL Servers" });
    const rows = list.getByRole("row");
    await expect(rows).toHaveCount(4);
    await expect(rows.nth(1)).toContainText("sql-prod-01");
    await expect(rows.nth(1)).toContainText("Receiving");
    await expect(rows.nth(1)).toContainText("current");
    await expect(rows.nth(2)).toContainText("Login failed");
    await expect(rows.nth(2)).toContainText("0.2.0 available");
    await expect(list.getByRole("link", { name: "sql-prod-01" })).toHaveAttribute(
      "href",
      /^\/integrations\/sqlserver\/s1/,
    );
    await expect(page.getByRole("link", { name: "+ Add SQL Server" })).toHaveAttribute(
      "href",
      "/integrations/sqlserver/new",
    );

    if (testInfo.project.name !== "phone") {
      await expect(
        page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Integrations" }),
      ).toHaveAttribute("aria-current", "page");
    }

    expect(await scrollsSideways(page)).toBe(false);
  });

  test("tests the connection through the agent, saying first that its Collector restarts", async ({ page }) => {
    await openConsole(page);
    await page.goto("/integrations/sqlserver/new");

    const connection = page.getByRole("region", { name: "1. Connect to SQL Server" });
    await expect(connection).toContainText("its Collector restarts twice for a few seconds");
    await expect(
      page.getByRole("navigation", { name: "Steps" }).getByRole("button", { name: /Connection/ }),
    ).toHaveAttribute("aria-current", "step");

    // Testing without a host asks for it instead of starting a probe.
    await page.getByRole("button", { name: "Test connection" }).first().click();
    await expect(page.getByRole("alert", { name: "What the step still needs" })).toContainText(
      "Enter the server's host.",
    );

    await connection.getByLabel("Server (host)").fill("sql-prod-01.corp.local");
    const started = page.waitForRequest(
      (request) => request.url().endsWith("/api/v1/integrations/probes") && request.method() === "POST",
    );
    await page.getByRole("button", { name: "Test connection" }).first().click();
    const body = (await started).postDataJSON() as Record<string, unknown>;
    expect(body).toMatchObject({
      address: "sql-prod-01.corp.local",
      port: 1433,
      instance: null,
      authentication: "sql",
      login: "veyra_monitor",
      agentId: builtInAgent.id,
    });

    await expect(connection.getByRole("status")).toContainText("Testing through the built-in agent");
    await expect(connection.getByRole("status")).toContainText("Connection successful", probeWait);
    await expect(connection.getByRole("status")).toContainText(
      "SQL Server 2022 CU14 (16.0.4135.4) · Enterprise · 96 databases · no permission missing",
    );
    await expect(connection).toContainText(/tested \d\d:\d\d by the built-in agent/);
    await expect(connection.getByRole("button", { name: "Test again" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("walks the five steps and saves the instance, the Windows sign-in, and the modules", async ({ page }) => {
    await openConsole(page);
    await page.goto("/integrations/sqlserver/new");

    const connection = page.getByRole("region", { name: "1. Connect to SQL Server" });
    await connection.getByLabel("Server (host)").fill("sql-prod-01.corp.local");
    await connection.getByRole("group", { name: "Instance" }).getByRole("button", { name: "Named instance" }).click();
    await connection.getByLabel("Instance name").fill("REPORTS");

    // Integrated sign-in is only offered once a Windows agent reads the server.
    const authentication = connection.getByLabel("Authentication");
    await expect(authentication.locator("option")).toHaveCount(2);
    await connection.getByLabel("Read by").selectOption(windowsAgent.id);
    await expect(authentication.locator("option")).toHaveCount(3);
    await authentication.selectOption("ntlm");
    await connection.getByLabel("Domain login").fill("CORP\\veyra_monitor");
    await connection.getByLabel("Password", { exact: true }).fill("Secret-Pass-123");

    const summary = page.getByRole("region", { name: "Integration summary" });
    await expect(summary).toContainText("sql-prod-01\\REPORTS");
    await expect(summary).toContainText("Windows (NTLM)");
    await expect(summary).toContainText("9 of 9");

    await page.getByRole("button", { name: "Next: permissions →" }).click();
    await expect(page.getByLabel("Login script", { exact: true })).toContainText(
      "CREATE LOGIN [CORP\\veyra_monitor] FROM WINDOWS;",
    );

    await page.getByRole("button", { name: "Next: modules →" }).click();
    const modules = page.getByRole("region", { name: "3. Modules" });
    await expect(modules.getByRole("checkbox", { name: "Core" })).toBeDisabled();
    await modules.getByRole("checkbox", { name: "Events" }).uncheck();
    await modules.getByRole("combobox", { name: "Queries: how often" }).selectOption("1800");
    await expect(summary).toContainText("8 of 9");

    await page.getByRole("button", { name: "Next: validation →" }).click();
    const validation = page.getByRole("list", { name: "Validation" });
    await expect(validation).toContainText("Every permission granted", probeWait);
    await expect(validation).toContainText("96 databases: master, model, msdb, tempdb, orders, billing, …");

    await page.getByRole("button", { name: "Next: installation →" }).click();
    await expect(page.getByLabel("Name in Veyra")).toHaveAttribute("placeholder", "sql-prod-01-reports");

    const saved = page.waitForRequest(
      (request) => request.url().endsWith("/api/v1/sql-servers") && request.method() === "POST",
    );
    await page.getByRole("button", { name: "Install", exact: true }).click();
    const input = (await saved).postDataJSON() as {
      modules: { id: string; enabled: boolean; intervalSeconds: number }[];
    } & Record<string, unknown>;
    expect(input).toMatchObject({
      name: "sql-prod-01-reports",
      address: "sql-prod-01.corp.local",
      instance: "REPORTS",
      authentication: "ntlm",
      login: "CORP\\veyra_monitor",
      password: "Secret-Pass-123",
      agentId: windowsAgent.id,
    });
    expect(input.modules.find((module) => module.id === "events")?.enabled).toBe(false);
    expect(input.modules.find((module) => module.id === "queries")?.intervalSeconds).toBe(1800);
    expect(input.modules.find((module) => module.id === "core")?.enabled).toBe(true);

    const installation = page.getByRole("region", { name: "5. Installation" });
    await expect(installation.getByRole("status")).toContainText("Installed: Veyra is watching", probeWait);
    await expect(installation.getByRole("link", { name: "Open the package page →" })).toHaveAttribute(
      "href",
      "/integrations/sqlserver/s4",
    );
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("shows the package page with its version, checks, health, and next steps", async ({ page }) => {
    await openConsole(page);
    await page.goto("/integrations/sqlserver/s1");

    await expect(page.getByRole("heading", { level: 1 })).toContainText("veyra-sqlserver-integration");
    await expect(page.getByRole("heading", { level: 1 })).toContainText("0.2.0");
    await expect(page.getByText("Installed · 0.2.0 active")).toBeVisible();
    await expect(page.getByRole("region", { name: "Installation" })).toContainText("by Dana K.");
    await expect(page.getByRole("region", { name: "Collector service" })).toContainText("up 3 d 14 h");
    await expect(page.getByRole("region", { name: "Configuration validation" })).toContainText("Valid");

    const checks = page.getByRole("region", { name: "Active checks" });
    await expect(checks).toContainText("main counters");
    await expect(checks).toContainText("142 ms");
    await expect(page.getByRole("region", { name: "Collected metrics" })).toContainText("38 metrics");
    await expect(
      page.getByRole("region", { name: "Key SQL Server health" }).getByRole("img", { name: /^CPU:/ }),
    ).toBeVisible();
    await expect(page.getByRole("region", { name: "Package information" })).toContainText("otelcol-contrib ≥ 0.140.0");

    const next = page.getByRole("region", { name: "Next steps" });
    await expect(next).toContainText("Done: Add a notification channel");
    await expect(next.getByRole("link", { name: "Review retention and sampling" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("validates now, rolls back to the previous version, and saves the modules", async ({ page }) => {
    await openConsole(page);
    await page.goto("/integrations/sqlserver/s1");

    const validated = page.waitForRequest(
      (request) => request.url().endsWith("/api/v1/integrations/probes") && request.method() === "POST",
    );
    await page.getByRole("button", { name: "Validate now" }).click();
    expect((await validated).postDataJSON()).toEqual({ serverId: "s1" });
    const validation = page.getByRole("region", { name: "Configuration validation" });
    await expect(validation).toContainText("Validating…");
    await expect(validation).toContainText("96 databases", probeWait);

    const rolledBack = page.waitForRequest(
      (request) => request.url().endsWith("/api/v1/integrations/sqlserver/s1/version") && request.method() === "POST",
    );
    await page.getByRole("button", { name: "Roll back to 0.1.0" }).click();
    expect((await rolledBack).postDataJSON()).toEqual({ version: "0.1.0" });
    await expect(page.getByRole("button", { name: "Update to 0.2.0", exact: true })).toBeEnabled();
    await expect(page.getByRole("heading", { level: 1 })).toContainText("0.1.0");

    await page.getByRole("button", { name: "Edit" }).click();
    await expect(page).toHaveURL(/tab=configuration/);
    const modules = page.getByRole("region", { name: "Modules" });
    await modules.getByRole("checkbox", { name: "Jobs" }).uncheck();
    const put = page.waitForRequest(
      (request) => request.url().endsWith("/api/v1/sql-servers/s1") && request.method() === "PUT",
    );
    await page.getByRole("button", { name: "Save changes" }).click();
    const body = (await put).postDataJSON() as { password: unknown; modules: { id: string; enabled: boolean }[] };
    expect(body.password).toBeNull();
    expect(body.modules.find((module) => module.id === "jobs")?.enabled).toBe(false);
    await expect(modules).toContainText("Saved. The agent applies it within a minute.");
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("asks before removing the integration", async ({ page }) => {
    await openConsole(page);
    await page.goto("/integrations/sqlserver/s1");

    let deleted = false;
    page.on("request", (request) => {
      if (request.method() === "DELETE" && request.url().endsWith("/api/v1/sql-servers/s1")) {
        deleted = true;
      }
    });

    await page.getByRole("button", { name: "Remove integration" }).click();
    const dialog = page.getByRole("alertdialog", { name: "Remove sql-prod-01?" });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Keep it" })).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
    expect(deleted).toBe(false);

    await page.getByRole("button", { name: "Remove integration" }).click();
    const removal = page.waitForRequest(
      (request) => request.method() === "DELETE" && request.url().endsWith("/api/v1/sql-servers/s1"),
    );
    await dialog.getByRole("button", { name: "Remove sql-prod-01" }).click();
    await removal;
    await expect(page).toHaveURL(/\/integrations$/);
  });

  test("shows the permissions and the history on their tabs", async ({ page }) => {
    await openConsole(page);
    await page.goto("/integrations/sqlserver/s1?tab=permissions");

    await expect(page.getByRole("region", { name: "Permissions" })).toContainText(
      "veyra_monitor has every permission Veyra reads with",
    );
    await expect(page.getByLabel("Grants script", { exact: true })).toContainText(
      "GRANT VIEW SERVER STATE TO [veyra_monitor];",
    );

    await page.getByRole("navigation", { name: "Integration" }).getByRole("button", { name: "History" }).click();
    await expect(page.getByRole("region", { name: "History" })).toContainText("Template updated from 0.1.0 to 0.2.0");
    expect(await scrollsSideways(page)).toBe(false);
  });

  test("Forecast keeps Settings for adding and links its servers to the same package page", async ({ page }) => {
    await answerApi(page, { look: "forecast" });
    await page.goto("/settings?section=Databases");
    await expect(page.getByRole("link", { name: "Package page of sql-prod-01" })).toHaveAttribute(
      "href",
      "/integrations/sqlserver/s1",
    );

    await page.goto("/integrations/sqlserver/s1");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("veyra-sqlserver-integration 0.2.0");
    await expect(page.getByRole("region", { name: "Active checks" })).toContainText("63 ms per run");

    await page.goto("/integrations/sqlserver/new");
    await expect(page.getByRole("link", { name: "Open Database monitoring" })).toHaveAttribute(
      "href",
      "/settings?section=Databases",
    );
    expect(await scrollsSideways(page)).toBe(false);
  });

  for (const [name, path] of [
    ["the list", "/integrations"],
    ["the wizard", "/integrations/sqlserver/new"],
    ["the package page", "/integrations/sqlserver/s1"],
  ] as const) {
    test(`explains every panel of ${name} inside the window`, async ({ page }) => {
      await openConsole(page);
      await page.goto(path);
      const buttons = page.locator("main").getByRole("button", { name: /^What .* means$/ });
      await buttons.first().waitFor();
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
        await page.keyboard.press("Escape");
        await expect(tip).toBeHidden();
        opened += 1;
      }

      expect(opened).toBeGreaterThan(0);
    });
  }
});
