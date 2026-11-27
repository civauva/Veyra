import { render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { openDiskFilling, openWarning, openWarningContext, resolvedLatency } from "../../../alerts/testAlerts";
import type * as client from "../../../api/client";
import { api } from "../../../api/client";
import type { Role } from "../../../api/types";
import { SessionContext, type SessionValue } from "../../../session/SessionContext";
import { setDisplayZone } from "../../../time/zone";
import { ConsoleAlert } from "./ConsoleAlert";

vi.mock("../../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { alert: vi.fn(), alertContext: vi.fn() } };
});

function renderAs(role: Role, id = openWarning.id) {
  const value = {
    state: {
      status: "signedIn",
      session: { id: "1", email: "a@example.com", displayName: "Ana", role, timeZone: "UTC", look: null },
    },
  } as unknown as SessionValue;

  return render(
    <SessionContext value={value}>
      <MemoryRouter initialEntries={[`/alerts/${id}`]}>
        <Routes>
          <Route path="/alerts/:id" element={<ConsoleAlert />} />
        </Routes>
      </MemoryRouter>
    </SessionContext>,
  );
}

beforeEach(() => {
  setDisplayZone("UTC");
  localStorage.setItem("veyra.view", "advanced");
  vi.mocked(api.alert).mockResolvedValue({ ...openWarning, incidentId: "inc-42", incidentKey: "INC-42" });
  vi.mocked(api.alertContext).mockResolvedValue(openWarningContext);
});

describe("ConsoleAlert", () => {
  it("ConsoleAlert_Open_ShowsTheHeadFactsAndWaysOnward", async () => {
    renderAs("Admin");

    expect(await screen.findByRole("heading", { level: 1 })).toHaveTextContent("Warning · open");
    expect(screen.getByText("Rule: Error rate")).toBeVisible();
    expect(screen.getByRole("link", { name: "Open payments-gateway" })).toHaveAttribute(
      "href",
      "/metrics/services/payments-gateway",
    );
    expect(screen.getByRole("link", { name: "Incident INC-42" })).toHaveAttribute("href", "/incidents/inc-42");
    expect(screen.getByRole("link", { name: "Rule settings" })).toHaveAttribute("href", "/settings?section=Rules");
  });

  it("ConsoleAlert_Viewer_HasNoRuleSettings", async () => {
    renderAs("Viewer");

    await screen.findByRole("heading", { level: 1 });
    expect(screen.queryByRole("link", { name: "Rule settings" })).not.toBeInTheDocument();
  });

  it("ConsoleAlert_Context_ShowsTheLogsAroundItAndTheAffectedBlocks", async () => {
    renderAs("Viewer");

    const logs = await screen.findByRole("region", { name: "Logs around this" });
    expect(await within(logs).findByText("Payment declined by PSP: upstream timeout after 1800 ms")).toBeVisible();
    expect(within(logs).getByText("2 of 31")).toBeVisible();
    expect(within(logs).getByRole("link", { name: "Open in Logs →" })).toHaveAttribute(
      "href",
      `/logs?${new URLSearchParams({
        q: "service:payments-gateway AND level>=warning",
        from: openWarningContext.from,
        to: openWarningContext.to,
      }).toString()}`,
    );

    const affected = screen.getByRole("region", { name: "Affected" });
    expect(within(affected).getByRole("row", { name: /payments-gateway/ })).toHaveTextContent("Warning");
    expect(within(affected).getByRole("row", { name: /orders-db/ })).toHaveTextContent("Caution");
    expect(within(affected).getByRole("row", { name: /web-02/ })).toHaveTextContent("Healthy");
    expect(api.alertContext).toHaveBeenCalledWith(openWarning.id, expect.anything());
  });

  it("ConsoleAlert_Open_ExplainsWhatItMeansHowToFixAndTheRule", async () => {
    renderAs("Viewer");

    expect(await screen.findByRole("region", { name: "What it means" })).toBeVisible();
    expect(screen.getByRole("region", { name: "How to fix" })).toBeVisible();

    const rule = screen.getByRole("region", { name: "Rule" });
    expect(within(rule).getByText("Caution when")).toBeVisible();
    expect(within(rule).getByText("#ops-alerts, On-call, Incident bot")).toBeVisible();
  });

  it("ConsoleAlert_DiskAlert_ShowsTheDiskFiguresAndItsChart", async () => {
    vi.mocked(api.alert).mockResolvedValue(openDiskFilling);
    renderAs("Viewer", openDiskFilling.id);

    const figures = await screen.findByRole("region", { name: "Figures" });
    expect(within(figures).getByText("Full in")).toBeVisible();
    expect(screen.getByRole("region", { name: /Used space and forecast/ })).toBeVisible();
  });

  it("ConsoleAlert_SimpleView_KeepsTheTimelineToAdvanced", async () => {
    localStorage.setItem("veyra.view", "simple");
    vi.mocked(api.alert).mockResolvedValue(resolvedLatency);
    renderAs("Viewer", resolvedLatency.id);

    expect(await screen.findByRole("heading", { level: 1 })).toHaveTextContent("Resolved");
    expect(screen.queryByRole("region", { name: "Timeline" })).not.toBeInTheDocument();
    expect(screen.getByText(/The timeline is in the Advanced view/)).toBeVisible();
  });
});
