import { render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { SessionProvider } from "../session/SessionProvider";
import { historyItems, historySummary } from "./testAlertHistory";
import { RuleScreen } from "./RuleScreen";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: { setupStatus: vi.fn(), currentSession: vi.fn(), alerts: vi.fn(), alertSummary: vi.fn() },
  };
});

function renderAt(path: string) {
  return render(
    <SessionProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/alerts/rules/:rule" element={<RuleScreen />} />
        </Routes>
      </MemoryRouter>
    </SessionProvider>,
  );
}

beforeEach(() => {
  vi.mocked(api.setupStatus).mockResolvedValue({ required: false });
  vi.mocked(api.currentSession).mockResolvedValue({
    id: "1",
    email: "admin@example.com",
    displayName: "Ana Admin",
    role: "Admin",
    timeZone: null,
    look: { chosen: null, default: "console", mayChoose: true },
  });
  vi.mocked(api.alertSummary).mockResolvedValue(historySummary);
  vi.mocked(api.alerts).mockResolvedValue({ items: historyItems.slice(0, 1), next: null });
});

describe("RuleScreen", () => {
  it("RuleScreen_SubjectGiven_ExplainsTheRuleAndHowToFixItForThatSubject", async () => {
    renderAt("/alerts/rules/error-rate?kind=service&name=payments-gateway");

    expect(await screen.findByRole("heading", { level: 1, name: "Error rate" })).toBeVisible();
    expect(screen.getByText(/About payments-gateway/)).toBeVisible();
    expect(screen.getByRole("heading", { name: "What it means" })).toBeVisible();
    const fix = screen.getByRole("region", { name: /How to fix/ });
    expect(within(fix).getAllByRole("listitem").length).toBeGreaterThan(0);
    expect(await screen.findByText("5%")).toBeVisible();
    expect(await screen.findByRole("link", { name: /Change the thresholds/ })).toHaveAttribute(
      "href",
      "/settings?section=Rules",
    );
  });

  it("RuleScreen_SubjectGiven_AsksForItsOpenAlertsOfTheRule", async () => {
    renderAt("/alerts/rules/error-rate?kind=service&name=payments-gateway");

    await waitFor(() =>
      expect(api.alerts).toHaveBeenCalledWith(
        expect.objectContaining({ state: "open", rules: ["error-rate"], kind: "service", block: "payments-gateway" }),
        null,
        expect.anything(),
      ),
    );
    const open = await screen.findByRole("region", { name: "Open alerts of payments-gateway" });
    // Each open alert opens at its own fix.
    expect(within(open).getByRole("link").getAttribute("href")).toMatch(/^\/alerts\/[^/]+#fix$/);
  });

  it("RuleScreen_UnknownRule_SaysSo", async () => {
    renderAt("/alerts/rules/no-such-rule");

    expect(await screen.findByText("Veyra has no rule by that name.")).toBeVisible();
  });
});
