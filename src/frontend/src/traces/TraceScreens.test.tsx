import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { checkoutId, checkoutTrace, durations, pspLog, testSpan, tracesPage } from "./testTraces";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      listTraces: vi.fn(),
      traceDurations: vi.fn(),
      traceServices: vi.fn(),
      trace: vi.fn(),
      searchLogs: vi.fn(),
    },
  };
});

const viewer = { id: "1", email: "dana@example.com", displayName: "Dana Kim", role: "Viewer" as const, timeZone: null };

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  localStorage.clear();
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.listTraces).mockResolvedValue(tracesPage);
  vi.mocked(api.traceDurations).mockResolvedValue(durations);
  vi.mocked(api.traceServices).mockResolvedValue([
    { service: "checkout-web", traces: 1 },
    { service: "orders-api", traces: 1 },
  ]);
  vi.mocked(api.trace).mockResolvedValue(checkoutTrace);
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [pspLog], nextCursor: null });
});

describe("TracesScreen", () => {
  it("is in the sidebar between Logs and Metrics", async () => {
    renderAt("/traces");

    const links = within(await screen.findByRole("navigation", { name: "Main" })).getAllByRole("link");
    const names = links.map((link) => link.textContent);

    expect(names.indexOf("Traces")).toBe(names.indexOf("Logs") + 1);
    expect(names.indexOf("Metrics")).toBe(names.indexOf("Traces") + 1);
  });

  it("lists each trace with why it failed and how long it took", async () => {
    renderAt("/traces");

    const row = (await screen.findByText("POST /api/checkout")).closest("a");
    expect(row).toHaveAttribute("href", `/traces/${checkoutId}?at=2026-11-09T12%3A00%3A00.000Z`);
    expect(row).toHaveTextContent("psp-client: The PSP did not answer");
    expect(row).toHaveTextContent("1.84 s");
    expect(row).toHaveTextContent("4 failed");
    expect(screen.getByRole("img", { name: /3 traces: 1 failed, 1 slower than the 95th percentile/ })).toBeVisible();
  });

  it("asks for failed traces when Failed is chosen", async () => {
    const user = userEvent.setup();
    renderAt("/traces");

    await user.click(await screen.findByRole("button", { name: "Failed · 1" }));

    const params = vi.mocked(api.listTraces).mock.calls.at(-1)?.[0];
    expect(params?.get("failed")).toBe("true");
  });

  it("opens a pasted trace ID instead of searching for it", async () => {
    const user = userEvent.setup();
    renderAt("/traces");

    await user.type(
      await screen.findByRole("searchbox", { name: "Trace ID or name" }),
      `${checkoutId.toUpperCase()}{Enter}`,
    );

    expect(await screen.findByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toBeVisible();
    expect(api.trace).toHaveBeenCalledWith(checkoutId, null, expect.anything());
  });
});

describe("TraceScreen", () => {
  it("shows the trace's numbers and opens on the span that caused the failure", async () => {
    renderAt(`/traces/${checkoutId}?at=2026-11-09T12:00:00.000Z`);

    expect(await screen.findByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toBeVisible();
    expect(screen.getByText("Duration").parentElement).toHaveTextContent("1.84 s");
    expect(screen.getByText("Failed").parentElement).toHaveTextContent("3");

    const panel = screen.getByRole("complementary", { name: "POST /authorize" });
    expect(within(panel).getByText("System.TimeoutException")).toBeVisible();
    expect(within(panel).getByText("The PSP did not answer within 1.5 s")).toBeVisible();
    expect(await within(panel).findByText(pspLog.body)).toBeVisible();
  });

  it("opens on the span with the exception when the failed call below it only says timeout", async () => {
    const timeout = testSpan("0000000000000006", "0000000000000003", "psp-client", "HTTPS POST psp", 120, 1500, {
      status: "Error",
      statusMessage: "timeout",
    });
    vi.mocked(api.trace).mockResolvedValue({ traceId: checkoutId, spans: [...checkoutTrace.spans, timeout] });
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("complementary", { name: "POST /authorize" })).toBeVisible();
  });

  it("says when part of the trace has not arrived", async () => {
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("status")).toHaveTextContent("1 span of inventory point to a parent");
    expect(screen.getByRole("button", { name: /reserve stock/ })).toHaveTextContent("parent not received");
  });

  it("hides and shows the spans under a span", async () => {
    const user = userEvent.setup();
    renderAt(`/traces/${checkoutId}`);

    await user.click(await screen.findByRole("button", { name: "Hide the 2 spans under POST /payments" }));
    expect(screen.queryByRole("button", { name: "POST /authorize" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Show the 2 spans under POST /payments" }));
    expect(screen.getByRole("button", { name: "POST /authorize" })).toBeVisible();
  });

  it("lists the trace's log records and selects a record's span from them", async () => {
    const user = userEvent.setup();
    renderAt(`/traces/${checkoutId}`);

    await user.click(await screen.findByRole("tab", { name: /Logs of this trace/ }));
    await user.click(await screen.findByRole("button", { name: "psp-client · POST /authorize" }));

    expect(screen.getByRole("tab", { name: "Spans" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("complementary", { name: "POST /authorize" })).toBeVisible();
  });

  it("says so when the trace is unknown", async () => {
    vi.mocked(api.trace).mockRejectedValue(new ApiError(404, "No span of this trace is stored."));
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("heading", { name: "Trace not found" })).toBeVisible();
  });

  it("keeps the list's filters to time, what to show, and services in the Simple view", async () => {
    renderAt("/traces");

    expect(await screen.findByRole("region", { name: "Show" })).toBeVisible();
    expect(screen.queryByRole("region", { name: "Duration in ms" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add attribute filter" })).not.toBeInTheDocument();
  });

  it("shows the duration and attribute filters in the Advanced view", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt("/traces");

    expect(await screen.findByRole("region", { name: "Duration in ms" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Add attribute filter" })).toBeVisible();
  });

  it("shows a duration filter that is in use, even in the Simple view", async () => {
    renderAt("/traces?min=500");

    expect(await screen.findByRole("region", { name: "Duration in ms" })).toBeVisible();
  });

  it("keeps a trace to its spans and logs in the Simple view, with its attributes in Advanced", async () => {
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("tab", { name: "Spans" })).toBeVisible();
    expect(screen.queryByRole("tab", { name: "Attributes" })).not.toBeInTheDocument();
  });
});
