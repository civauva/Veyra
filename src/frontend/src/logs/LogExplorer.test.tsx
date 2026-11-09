import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { LogEntry } from "../api/types";
import { LogExplorer } from "./LogExplorer";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { searchLogs: vi.fn(), summarizeLogs: vi.fn() } };
});

/** A record as the API sends it, with only what a test cares about to change. */
function entry(overrides: Partial<LogEntry>): LogEntry {
  return {
    id: 1,
    timestamp: "2026-11-09T07:34:12.418Z",
    observedTimestamp: "2026-11-09T07:34:12.420Z",
    severity: "Information",
    severityText: null,
    body: "Order 88213 created",
    serviceName: "orders-api",
    environment: "production",
    traceId: null,
    spanId: null,
    attributes: {},
    resourceAttributes: { "service.name": "orders-api" },
    ...overrides,
  };
}

const declined = entry({
  id: 2,
  severity: "Error",
  body: "Payment declined by PSP: upstream timeout after 1800 ms",
  serviceName: "payments-gateway",
  traceId: "5b8efff798038103d269b633813fc60c",
  spanId: "eee19b7ec3c1b174",
  attributes: {
    "http.route": "/checkout/confirm",
    "exception.stacktrace": "System.TimeoutException: The PSP did not answer\n   at PspClient.AuthorizeAsync()",
  },
  resourceAttributes: { "service.name": "payments-gateway", "host.name": "web-02", "service.version": "2.14.1" },
});

const created = entry({ id: 1 });

/** The query of the latest call to a mocked API function. */
function lastParams(fn: typeof api.searchLogs | typeof api.summarizeLogs): URLSearchParams {
  const call = vi.mocked(fn).mock.calls.at(-1);

  if (!call) {
    throw new Error("The API was not called.");
  }

  return call[0];
}

/** Shows the router location, so tests can check that filters reach the URL. */
function Location() {
  const location = useLocation();

  return <output data-testid="location">{location.search}</output>;
}

function renderExplorer(search = "") {
  return render(
    <MemoryRouter initialEntries={[`/logs${search}`]}>
      <Routes>
        <Route
          path="/logs"
          element={
            <>
              <LogExplorer />
              <Location />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

beforeAll(() => {
  // jsdom lays nothing out. The virtual list needs a viewport size to decide which rows to render.
  Object.defineProperty(HTMLElement.prototype, "clientHeight", { configurable: true, get: () => 800 });
  Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 1000 });
});

beforeEach(() => {
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [declined, created], nextCursor: null });
  vi.mocked(api.summarizeLogs).mockResolvedValue({
    bucketSeconds: 120,
    buckets: [
      { start: "2026-11-09T07:30:00Z", total: 3000, errors: 40 },
      { start: "2026-11-09T07:32:00Z", total: 118, errors: 2 },
    ],
    services: [
      { name: "payments-gateway", count: 4812 },
      { name: "orders-api", count: 1650 },
    ],
  });
});

afterEach(() => vi.useRealTimers());

describe("LogExplorer", () => {
  it("shows the newest records of the last hour with the total from the summary", async () => {
    renderExplorer();

    expect(await screen.findByRole("button", { name: /Payment declined by PSP/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Order 88213 created/ })).toBeInTheDocument();
    expect(screen.getByText(/Showing 2 of 3,118/)).toBeInTheDocument();

    const params = lastParams(api.searchLogs);
    const window = Date.parse(params.get("to") ?? "") - Date.parse(params.get("from") ?? "");
    expect(window).toBe(60 * 60_000);
  });

  it("draws the histogram with errors, for screen readers too", async () => {
    renderExplorer();

    expect(await screen.findByRole("img", { name: /3,118 records, 42 errors/ })).toBeInTheDocument();
  });

  it("filters by minimum severity and keeps the choice in the URL", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: "Warning" }));

    await waitFor(() => expect(lastParams(api.searchLogs).get("severity")).toBe("Warning"));
    expect(lastParams(api.summarizeLogs).get("severity")).toBe("Warning");
    expect(screen.getByTestId("location")).toHaveTextContent("severity=Warning");
    expect(screen.getByRole("button", { name: "Warning" })).toHaveAttribute("aria-pressed", "true");
  });

  it("lists services with their counts and filters by the ones ticked", async () => {
    const user = userEvent.setup();
    renderExplorer();

    const payments = await screen.findByRole("checkbox", { name: /payments-gateway/ });
    expect(screen.getByText("4,812")).toBeInTheDocument();
    await user.click(payments);

    await waitFor(() => expect(lastParams(api.searchLogs).getAll("service")).toEqual(["payments-gateway"]));
  });

  it("searches the message text when the person presses Enter", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.type(await screen.findByRole("searchbox", { name: "Search messages" }), "declined timeout{Enter}");

    await waitFor(() => expect(lastParams(api.searchLogs).get("text")).toBe("declined timeout"));
  });

  it("adds and removes attribute filters", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: "Add attribute filter" }));
    await user.type(screen.getByLabelText("Attribute"), "http.route");
    await user.type(screen.getByLabelText("Value"), "/checkout/confirm");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() =>
      expect(lastParams(api.searchLogs).getAll("attribute")).toEqual(["http.route:/checkout/confirm"]),
    );

    await user.click(screen.getByRole("button", { name: "Remove http.route = /checkout/confirm" }));

    await waitFor(() => expect(lastParams(api.searchLogs).getAll("attribute")).toEqual([]));
  });

  it("opens the selected record with its stack trace, attributes, resource, and trace", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: /Payment declined by PSP/ }));

    const detail = screen.getByRole("region", { name: "Selected record" });
    expect(within(detail).getByRole("heading", { name: /Payment declined by PSP/ })).toBeInTheDocument();
    expect(within(detail).getByText(/System.TimeoutException: The PSP did not answer/)).toBeInTheDocument();
    expect(within(detail).getByText("/checkout/confirm")).toBeInTheDocument();
    expect(within(detail).getByText("web-02")).toBeInTheDocument();
    expect(within(detail).getByText("5b8efff798038103d269b633813fc60c")).toBeInTheDocument();
  });

  it("follows the selected record's trace", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: /Payment declined by PSP/ }));
    await user.click(screen.getByRole("button", { name: "Same trace" }));

    await waitFor(() => expect(lastParams(api.searchLogs).get("traceId")).toBe("5b8efff798038103d269b633813fc60c"));
  });

  it("shows one minute around the selected record in its service", async () => {
    const user = userEvent.setup();
    renderExplorer("?severity=Error&text=declined");

    await user.click(await screen.findByRole("button", { name: /Payment declined by PSP/ }));
    await user.click(screen.getByRole("button", { name: "± 1 minute, this service" }));

    await waitFor(() => expect(lastParams(api.searchLogs).get("from")).toBe("2026-11-09T07:33:12.418Z"));
    const params = lastParams(api.searchLogs);
    expect(params.get("to")).toBe("2026-11-09T07:35:12.418Z");
    expect(params.getAll("service")).toEqual(["payments-gateway"]);
    expect(params.get("severity")).toBeNull();
    expect(params.get("text")).toBeNull();
  });

  it("copies the selected record as JSON", async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: /Payment declined by PSP/ }));
    await user.click(screen.getByRole("button", { name: "Copy JSON" }));

    expect(JSON.parse(writeText.mock.calls[0]?.[0] ?? "")).toEqual(declined);
    expect(await screen.findByText("Copied")).toBeInTheDocument();
  });

  it("closes the selected record with Escape", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: /Payment declined by PSP/ }));
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("region", { name: "Selected record" })).not.toBeInTheDocument();
  });

  it("loads the next page with the cursor of the previous one", async () => {
    vi.mocked(api.searchLogs)
      .mockResolvedValueOnce({ items: [declined], nextCursor: "page-2" })
      .mockResolvedValueOnce({ items: [created], nextCursor: null });
    const user = userEvent.setup();
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: "Load more" }));

    expect(await screen.findByRole("button", { name: /Order 88213 created/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Payment declined by PSP/ })).toBeInTheDocument();
    expect(lastParams(api.searchLogs).get("cursor")).toBe("page-2");
  });

  it("asks for new records every five seconds while following", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: (ms) => vi.advanceTimersByTime(ms) });
    renderExplorer();

    await user.click(await screen.findByRole("button", { name: "Follow new" }));
    const calls = vi.mocked(api.searchLogs).mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });

    expect(vi.mocked(api.searchLogs).mock.calls.length).toBeGreaterThan(calls);
    expect(screen.getByRole("button", { name: "Follow new" })).toHaveAttribute("aria-pressed", "true");
  });

  it("explains a failed search", async () => {
    vi.mocked(api.searchLogs).mockRejectedValue(new Error("The time window is too wide."));
    renderExplorer();

    expect(await screen.findByRole("alert")).toHaveTextContent("The time window is too wide.");
  });

  it("says so when nothing matches", async () => {
    vi.mocked(api.searchLogs).mockResolvedValue({ items: [], nextCursor: null });
    renderExplorer();

    expect(await screen.findByText(/No records match/)).toBeInTheDocument();
  });
});
