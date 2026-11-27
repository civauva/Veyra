import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../../api/client";
import { api } from "../../../api/client";
import { durations, tracesPage } from "../../../traces/testTraces";
import { ConsoleTraces } from "./ConsoleTraces";

vi.mock("../../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: { listTraces: vi.fn(), traceDurations: vi.fn(), traceServices: vi.fn(), traceSummary: vi.fn() },
  };
});

const summary = { traces: 18_402, previousTraces: 17_360, failed: 3, slow: 920, p50Ms: 62, p95Ms: 214, services: 9 };

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/traces" element={<ConsoleTraces />} />
        <Route path="/traces/:traceId" element={<p>One trace</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  localStorage.setItem("veyra.view", "advanced");
  vi.mocked(api.listTraces).mockResolvedValue(tracesPage);
  vi.mocked(api.traceDurations).mockResolvedValue(durations);
  vi.mocked(api.traceSummary).mockResolvedValue(summary);
  vi.mocked(api.traceServices).mockResolvedValue([
    { service: "checkout-web", traces: 96, p95Ms: 41_200, failed: 8 },
    { service: "orders-api", traces: 4210, p95Ms: 1120, failed: 2 },
  ]);
});

describe("ConsoleTraces", () => {
  it("ConsoleTraces_Opened_AsksForWorstFirstAndShowsTheServersFigures", async () => {
    renderAt("/traces");

    const figures = await screen.findByRole("region", { name: "Figures" });

    expect(await within(figures).findByText("18,402")).toBeVisible();
    expect(within(figures).getByText("↑ 6% vs the period before")).toBeVisible();
    expect(within(figures).getByText("· 0.02%")).toBeVisible();
    expect(within(figures).getByText("920")).toBeVisible();
    expect(within(figures).getByText("p95 214 ms over the range")).toBeVisible();
    expect(within(figures).getByText("62 ms")).toBeVisible();
    expect(vi.mocked(api.listTraces).mock.calls[0]?.[0].get("order")).toBe("worst");
    expect(vi.mocked(api.traceServices).mock.calls[0]?.[0].get("order")).toBe("slowest");
    expect(screen.getByRole("heading", { name: /Traces/, level: 1 })).toHaveTextContent("3 failed · 920 slow");
  });

  it("ConsoleTraces_ServiceClicked_FiltersTheListByIt", async () => {
    const user = userEvent.setup();
    renderAt("/traces");

    const services = await screen.findByRole("region", { name: "Slowest services" });
    await user.click(await within(services).findByRole("button", { name: "checkout-web" }));

    await waitFor(() =>
      expect(vi.mocked(api.listTraces).mock.lastCall?.[0].getAll("service")).toEqual(["checkout-web"]),
    );
    expect(within(services).getByText("8.33%")).toBeVisible();
  });

  it("ConsoleTraces_OrderAndShowChosen_AreSentToTheServer", async () => {
    const user = userEvent.setup();
    renderAt("/traces");

    await screen.findByRole("region", { name: "Figures" });
    await user.selectOptions(screen.getByRole("combobox", { name: "Order" }), "newest");
    await waitFor(() => expect(vi.mocked(api.listTraces).mock.lastCall?.[0].has("order")).toBe(false));

    await user.selectOptions(screen.getByRole("combobox", { name: "Show" }), "failed-slow");
    await waitFor(() => expect(vi.mocked(api.listTraces).mock.lastCall?.[0].get("failed")).toBe("true"));
    expect(vi.mocked(api.listTraces).mock.lastCall?.[0].get("slow")).toBe("true");
  });

  it("ConsoleTraces_List_ShowsEachTraceWithItsStatus", async () => {
    renderAt("/traces");

    const table = await screen.findByRole("table", { name: "Traces" });

    expect(await within(table).findByRole("link", { name: "POST /api/checkout" })).toHaveAttribute(
      "href",
      `/traces/${tracesPage.items[0]?.traceId}?at=${encodeURIComponent("2026-11-09T12:00:00.000Z")}`,
    );
    expect(within(table).getByText("Failed")).toBeVisible();
    expect(within(table).getByText("OK")).toBeVisible();
    expect(screen.getByText("2 of 18,402")).toBeVisible();
  });

  it("ConsoleTraces_TraceIdPasted_OpensIt", async () => {
    const user = userEvent.setup();
    renderAt("/traces");

    await user.type(
      await screen.findByRole("searchbox", { name: "Operation, or paste a trace ID" }),
      "5B8EFFF798038103D269B633813FC60C{Enter}",
    );

    expect(await screen.findByText("One trace")).toBeVisible();
  });
});
