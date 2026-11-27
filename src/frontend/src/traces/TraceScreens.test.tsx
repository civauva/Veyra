import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { checkoutErrors, checkoutId, checkoutTrace, durations, pspLog, tracesPage } from "./testTraces";

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

const viewer = {
  id: "1",
  email: "dana@example.com",
  displayName: "Dana Kim",
  role: "Viewer" as const,
  timeZone: null,
  look: { chosen: null, default: "forecast", mayChoose: true },
};

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
    { service: "checkout-web", traces: 1, p95Ms: 1840, failed: 1 },
    { service: "orders-api", traces: 1, p95Ms: 3, failed: 0 },
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

  it("has the bar with its name and the view toggle", async () => {
    renderAt("/traces");

    const trail = await screen.findByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByText("Traces")).toHaveAttribute("aria-current", "page");
    expect(within(trail.parentElement as HTMLElement).getByRole("group", { name: "View" })).toBeInTheDocument();
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

  it("shows the counts the server sends, though the chart draws fewer points", async () => {
    vi.mocked(api.traceDurations).mockResolvedValue({ ...durations, read: 18_000, failed: 412, slow: 900 });
    renderAt("/traces");

    expect(await screen.findByRole("button", { name: "Failed · 412" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Slow · 900" })).toBeVisible();
    expect(screen.getByRole("img", { name: /18000 traces: 412 failed, 900 slower/ })).toBeVisible();
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
    expect(api.trace).toHaveBeenCalledWith(checkoutId, null, false, expect.anything());
  });
});

describe("TraceScreen", () => {
  it("shows the trace's numbers and opens on the span that caused the failure", async () => {
    renderAt(`/traces/${checkoutId}?at=2026-11-09T12:00:00.000Z`);

    expect(await screen.findByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toBeVisible();
    expect(screen.getByText("Duration").closest(".trace-stats > div")).toHaveTextContent("1.84 s");
    expect(screen.getByText("Failed").closest(".trace-stats > div")).toHaveTextContent("3");

    const panel = screen.getByRole("complementary", { name: "POST /authorize" });
    expect(within(panel).getByText("System.TimeoutException")).toBeVisible();
    expect(within(panel).getByText("The PSP did not answer within 1.5 s")).toBeVisible();
    expect(await within(panel).findByText(pspLog.body)).toBeVisible();
  });

  it("says in the bar that the trace is one of the traces, with the view toggle", async () => {
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("heading", { level: 1, name: /POST \/api\/checkout/ })).toBeVisible();
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Traces" })).toHaveAttribute("href", "/traces");
    expect(within(trail).getByText("POST /api/checkout")).toHaveAttribute("aria-current", "page");
    expect(within(trail.parentElement as HTMLElement).getByRole("group", { name: "View" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "‹ Traces" })).not.toBeInTheDocument();
  });

  it("opens on the span the server names as the cause", async () => {
    vi.mocked(api.trace).mockResolvedValue({
      ...checkoutTrace,
      summary: { ...checkoutTrace.summary, cause: "0000000000000004" },
    });
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("complementary", { name: "SELECT payments.customer" })).toBeVisible();
  });

  it("asks the server for the failed spans only, keeping the whole trace's figures", async () => {
    const user = userEvent.setup();
    vi.mocked(api.trace).mockImplementation((_id, _at, errorsOnly) =>
      Promise.resolve(errorsOnly ? checkoutErrors : checkoutTrace),
    );
    renderAt(`/traces/${checkoutId}`);

    await user.click(await screen.findByRole("button", { name: "Errors only" }));

    expect(api.trace).toHaveBeenLastCalledWith(checkoutId, null, true, expect.any(AbortSignal));
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: /SELECT payments.customer/ })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: "POST /authorize" })).toBeVisible();
    expect(screen.getByText("Spans", { selector: ".trace-stats dt span" }).closest("div")).toHaveTextContent("5");
  });

  it("asks the server for the records of the chosen span", async () => {
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("complementary", { name: "POST /authorize" })).toBeVisible();
    await waitFor(() =>
      expect(vi.mocked(api.searchLogs).mock.calls.some(([params]) => params.get("spanId") === "0000000000000003")).toBe(
        true,
      ),
    );
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

  it("asks for the trace's records oldest first and lists them in the server's order", async () => {
    const user = userEvent.setup();
    const later = { ...pspLog, id: 8, timestamp: "2026-11-09T12:00:01.900Z", body: "Retry gave up" };
    // The server sends them oldest first; a later record first here proves the screen does not sort them again.
    vi.mocked(api.searchLogs).mockResolvedValue({ items: [later, pspLog], nextCursor: null });
    renderAt(`/traces/${checkoutId}`);

    await user.click(await screen.findByRole("tab", { name: /Logs of this trace/ }));

    // The chosen span shows its own records too; only the trace's list counts here.
    const rows = (await screen.findAllByText(/Retry gave up|Authorization failed/)).filter((row) =>
      row.closest(".trace-logs"),
    );
    expect(rows.map((row) => row.textContent)).toEqual(["Retry gave up", pspLog.body]);
    expect(vi.mocked(api.searchLogs).mock.calls.every(([params]) => params.get("order") === "oldest")).toBe(true);
  });

  it("says so when the trace is unknown", async () => {
    vi.mocked(api.trace).mockRejectedValue(new ApiError(404, "No span of this trace is stored."));
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("heading", { name: "Trace not found" })).toBeVisible();
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Traces" })).toHaveAttribute("href", "/traces");
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

  it("explains the duration chart and the filters with a question mark", async () => {
    const user = userEvent.setup();
    renderAt("/traces");

    await user.click(await screen.findByRole("button", { name: "What Duration of each trace means" }));

    expect(screen.getByRole("tooltip")).toHaveTextContent(/95th percentile/);
    expect(screen.getByRole("button", { name: "What Show means" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "What Services means" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Show" })).toBeInTheDocument();
  });

  it("explains a trace's numbers, its critical path, and its span with a question mark", async () => {
    const user = userEvent.setup();
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("button", { name: "What Duration means" })).toBeInTheDocument();
    for (const name of ["Spans", "Services", "Failed", "Critical path"]) {
      expect(screen.getByRole("button", { name: `What ${name} means` })).toBeInTheDocument();
    }

    const panel = screen.getByRole("complementary", { name: "POST /authorize" });
    expect(within(panel).getByRole("button", { name: "What Attributes means" })).toBeInTheDocument();
    expect(within(panel).getByRole("region", { name: /Logs of this span/ })).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "What Logs of this span means" }));
    expect(screen.getByRole("tooltip")).toHaveTextContent(/span ID/);
  });

  it("keeps a trace to its spans and logs in the Simple view, with its attributes in Advanced", async () => {
    renderAt(`/traces/${checkoutId}`);

    expect(await screen.findByRole("tab", { name: "Spans" })).toBeVisible();
    expect(screen.queryByRole("tab", { name: "Attributes" })).not.toBeInTheDocument();
  });
});
