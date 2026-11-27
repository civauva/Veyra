import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../../api/client";
import { api, ApiError } from "../../../api/client";
import { checkoutErrors, checkoutId, checkoutTrace, pspLog } from "../../../traces/testTraces";
import { ConsoleTrace } from "./ConsoleTrace";

vi.mock("../../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { trace: vi.fn(), searchLogs: vi.fn() } };
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/traces/:traceId" element={<ConsoleTrace />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  localStorage.setItem("veyra.view", "advanced");
  vi.mocked(api.trace).mockImplementation((_id, _at, errorsOnly) =>
    Promise.resolve(errorsOnly ? checkoutErrors : checkoutTrace),
  );
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [pspLog], nextCursor: null });
});

describe("ConsoleTrace", () => {
  it("ConsoleTrace_Opened_ShowsTheHeadFactsAndFigures", async () => {
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toHaveTextContent("Failed");
    expect(screen.getByText("4bf92f3577…4736")).toBeVisible();

    const figures = screen.getByRole("region", { name: "Figures" });
    expect(within(figures).getByText("1.84 s")).toBeVisible();
    expect(within(figures).getByText("5 services · 3 levels")).toBeVisible();
    expect(within(figures).getByText("System.TimeoutException in psp-client")).toBeVisible();
    expect(within(figures).getByText("88%")).toBeVisible();
  });

  it("ConsoleTrace_Opened_DrawsTheWaterfallWithTheCriticalPathAndTheCauseSelected", async () => {
    renderAt(`/traces/${checkoutId}`);

    const waterfall = await screen.findByRole("list", { name: "Spans" });
    expect(within(waterfall).getAllByRole("listitem")).toHaveLength(5);
    expect(within(waterfall).getAllByTitle("On the critical path")).toHaveLength(4);

    const span = screen.getByRole("region", { name: "POST /authorize" });
    expect(within(span).getByText("The PSP did not answer within 1.5 s", { exact: false })).toBeVisible();
    expect(within(span).getByText("504")).toBeVisible();
  });

  it("ConsoleTrace_ErrorsOnly_AsksTheServerForTheFailedSpans", async () => {
    const user = userEvent.setup();
    renderAt(`/traces/${checkoutId}`);

    await user.click(await screen.findByRole("button", { name: "Errors only" }));

    await waitFor(() => expect(vi.mocked(api.trace).mock.lastCall?.[2]).toBe(true));
    expect(await screen.findByText(`3 of 5 spans`)).toBeVisible();
  });

  it("ConsoleTrace_LogsTab_ListsTheTracesRecords", async () => {
    const user = userEvent.setup();
    renderAt(`/traces/${checkoutId}`);

    await user.click(await screen.findByRole("tab", { name: /Logs of this trace/ }));

    expect(await screen.findByText(pspLog.body)).toBeVisible();
  });

  it("ConsoleTrace_Unknown_SaysItIsNotKept", async () => {
    vi.mocked(api.trace).mockRejectedValue(new ApiError(404, "Not found"));
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByText(/No span of trace/)).toBeVisible();
  });
});
