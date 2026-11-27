import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { hostsList, servicesList } from "../resources/testResources";
import { availabilityWords, OverviewScreen } from "./OverviewScreen";
import { overview } from "./testOverview";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { overview: vi.fn(), resourceServices: vi.fn(), resourceHosts: vi.fn() } };
});

function renderOverview() {
  return render(
    <MemoryRouter>
      <OverviewScreen />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(api.overview).mockResolvedValue(overview);
  vi.mocked(api.resourceServices).mockResolvedValue(servicesList);
  vi.mocked(api.resourceHosts).mockResolvedValue(hostsList);
});

describe("OverviewScreen", () => {
  it("OverviewScreen_Figures_ShowTheServersFiguresAndChanges", async () => {
    renderOverview();

    const figures = await screen.findByRole("region", { name: "Figures" });
    expect(await within(figures).findByText("1.24M")).toBeVisible();
    expect(within(figures).getByText("↑ 12% vs before")).toBeVisible();
    expect(within(figures).getByText("5 of 180 runs failed")).toBeVisible();
    expect(within(figures).getByText("99.95%")).toBeVisible();
  });

  it("OverviewScreen_Tables_ListEachSetWorstFirstAsTheServerOrdersIt", async () => {
    renderOverview();

    const applications = await screen.findByRole("region", { name: "Applications" });
    expect((await within(applications).findAllByRole("row"))[1]).toHaveTextContent("payments-gateway");
    const databases = screen.getByRole("region", { name: "SQL Server databases" });
    expect(within(databases).getAllByRole("row")[1]).toHaveTextContent("ReportingBackup late");
    expect(within(databases).getByRole("link", { name: "Reporting" })).toHaveAttribute(
      "href",
      "/metrics/databases/microsoft.sql_server/SQL-PROD-01?tab=databases",
    );
  });

  it("OverviewScreen_Pipelines_FailedFirstAndCollectorsWithoutAnInventedQueue", async () => {
    renderOverview();

    const pipelines = await screen.findByRole("region", { name: "Pipeline executions" });
    expect((await within(pipelines).findAllByRole("row"))[1]).toHaveTextContent("CustomerSync");
    expect(within(pipelines).getByText("Failed")).toBeVisible();
    // A run the server knows as a job opens the job's own screen (ADR 0027); others keep the server's Jobs tab.
    expect(within(pipelines).getByRole("link", { name: "CustomerSync" })).toHaveAttribute(
      "href",
      "/jobs/job-customersync",
    );
    expect(within(pipelines).getByRole("link", { name: "OrderExport" })).toHaveAttribute(
      "href",
      "/metrics/databases/microsoft.sql_server/SQL-PROD-01?tab=jobs",
    );
    const collectors = screen.getByRole("region", { name: "Collector health" });
    expect(within(collectors).getAllByRole("row")[1]).toHaveTextContent("Built-in");
    expect(within(collectors).getAllByTitle("Agents do not report their queue yet")[0]).toHaveTextContent("—");
  });

  it("OverviewScreen_Collectors_ShowEachStateInItsOwnColour", async () => {
    renderOverview();

    const collectors = await screen.findByRole("region", { name: "Collector health" });
    // An online agent is fine; the server says "Online", which the tile once showed in red (VEY-82).
    expect(await within(collectors).findByText("Online")).toHaveClass("ov-pill", "ok");
    expect(within(collectors).getByText("Offline")).toHaveClass("ov-pill", "bad");
  });

  it("OverviewScreen_Figures_OpenTheirDetails", async () => {
    renderOverview();

    const figures = await screen.findByRole("region", { name: "Figures" });
    await within(figures).findByText("1.24M");
    const opens = (name: string) => within(figures).getByRole("link", { name }).getAttribute("href");

    expect(opens("Services healthy")).toBe("/resources/services");
    expect(opens("Hosts online")).toBe("/resources/hosts");
    expect(opens("Database instances")).toBe("/resources/databases");
    expect(opens("Request volume")).toBe("/traces");
    expect(opens("Error rate")).toBe("/traces?show=failed");
    expect(opens("Open alerts")).toBe("/alerts");
    // Five runs failed in the fixture, so the tile opens the failing jobs.
    expect(opens("Jobs succeeded")).toBe("/jobs?status=failing");
    expect(opens("Availability")).toBe("/alerts");
  });

  it("OverviewScreen_Panels_OpenTheirFullScreensFromTheirTitles", async () => {
    renderOverview();

    await screen.findByText("1.24M");
    const opens = (panel: string) =>
      within(screen.getByRole("region", { name: panel }))
        .getByRole("link", { name: panel })
        .getAttribute("href");

    expect(opens("Needs attention")).toBe("/alerts");
    expect(opens("Request activity")).toBe("/traces");
    expect(opens("Telemetry ingestion")).toBe("/logs");
    expect(opens("Applications")).toBe("/resources/services");
    expect(opens("Infrastructure")).toBe("/resources/hosts");
    expect(opens("SQL Server databases")).toBe("/resources/databases");
    expect(opens("Pipeline executions")).toBe("/jobs");
    expect(opens("Host resource trends")).toBe("/resources/hosts");
    expect(opens("Collector health")).toBe("/settings?section=Agents");
  });

  it("OverviewScreen_Collectors_EachRowOpensTheAgents", async () => {
    renderOverview();

    const collectors = await screen.findByRole("region", { name: "Collector health" });
    expect(await within(collectors).findByRole("link", { name: "branch-office" })).toHaveAttribute(
      "href",
      "/settings?section=Agents",
    );
  });

  it("availabilityWords_NearTheTop_KeepsTwoDecimalsWithoutRoundingUp", () => {
    expect(availabilityWords(0.99995)).toBe("99.99%");
    expect(availabilityWords(0.9995)).toBe("99.95%");
    expect(availabilityWords(1)).toBe("100%");
    expect(availabilityWords(0.92)).toBe("92%");
    expect(availabilityWords(null)).toBe("—");
  });

  it("OverviewScreen_NotAnAdmin_SaysWhereAgentsAreListed", async () => {
    vi.mocked(api.overview).mockResolvedValue({ ...overview, collectors: null });
    renderOverview();

    expect(await screen.findByText("Agents are listed for Admins, on Settings → Agents.")).toBeVisible();
  });

  it("OverviewScreen_EveryTile_ExplainsItself", async () => {
    renderOverview();

    await screen.findByText("1.24M");
    for (const name of [
      "Services healthy",
      "Availability",
      "Request activity",
      "Telemetry ingestion",
      "Collector health",
    ]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeInTheDocument();
    }
  });
});
