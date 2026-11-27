import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { stormSnapshot } from "../home/testSnapshots";
import { SessionProvider } from "../session/SessionProvider";
import { setRuleCatalog } from "./ruleCatalog";
import {
  integrityRule,
  openIntegrity,
  openSevereError,
  openWarning,
  resolvedCaution,
  resolvedLatency,
} from "./testAlerts";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      alert: vi.fn(),
      searchLogs: vi.fn(),
      summarizeLogs: vi.fn(),
    },
  };
});

const viewer = {
  id: "1",
  email: "viewer@example.com",
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
  vi.mocked(api.currentSession).mockResolvedValue(viewer);
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [], nextCursor: null });
  vi.mocked(api.summarizeLogs).mockResolvedValue({ bucketSeconds: 60, buckets: [], services: [], total: 0, errors: 0 });
  localStorage.clear();
  // Advanced is a superset; the Simple view has its own tests below.
  localStorage.setItem("veyra.view", "advanced");
});

describe("AlertScreen", () => {
  it("tells what the alert says, when it crossed each threshold, and paints the storm", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    const { container } = renderAt(`/alerts/${openWarning.id}`);

    expect(
      await screen.findByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Open for 36 min · warning")).toBeVisible();
    expect(screen.getByText(/went above 1% at 06:58 and above 5% at 07:01/)).toBeVisible();
    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-storm"));
    expect(vi.mocked(api.alert)).toHaveBeenCalledWith(openWarning.id, expect.any(AbortSignal));
  });

  it("links a service's alert to its failing or slow traces of the alert's window", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);
    const { unmount } = renderAt(`/alerts/${openWarning.id}`);

    // Ten minutes before it opened to five after its last check, as the trace list writes a picked window.
    expect(await screen.findByRole("link", { name: "Its failed traces" })).toHaveAttribute(
      "href",
      "/traces?show=failed&service=payments-gateway&from=2026-11-10T06%3A48%3A00.000Z&to=2026-11-10T07%3A39%3A00.000Z",
    );
    unmount();

    vi.mocked(api.alert).mockResolvedValue(resolvedLatency);
    renderAt(`/alerts/${resolvedLatency.id}`);

    expect(await screen.findByRole("link", { name: "Its slow traces" })).toHaveAttribute(
      "href",
      expect.stringContaining("/traces?show=slow&service=checkout-web&from="),
    );
  });

  it("shows the five facts with the channels that were told", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    const peak = (await screen.findByText("Peak")).closest("div");
    expect(peak).toHaveTextContent("24.1%at 07:12");
    const notified = screen.getByText("Notified").closest("div");
    expect(notified).toHaveTextContent("3#ops-alerts, On-call, Incident bot");
  });

  it("lists every change with its messages, and why a failing one has not gone through", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    const timeline = await screen.findByRole("region", { name: "Timeline" });
    const rows = within(timeline).getAllByRole("listitem");
    expect(rows.map((row) => row.textContent)).toEqual([
      "06:58Opened as caution1.8% errors",
      "06:58Webhook #ops-alertsFirst trydelivered · 204",
      "07:01Rose to warning6.2% errors",
      "07:01Webhook #ops-alertsFirst trydelivered · 204",
      "07:01Email On-callFirst trysent",
      "07:04Webhook Incident botTry 3 failed: Timed out after 10 s. Next try 07:12.retrying",
    ]);
  });

  it("draws the chart with a summary for screen readers", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    expect(
      await screen.findByRole("img", {
        name:
          "Error rate of payments-gateway from 06:28 to 07:34 UTC, peak 24.1% at 07:12, " +
          "warning from 5%, caution from 1%.",
      }),
    ).toBeInTheDocument();
  });

  it("links the most frequent errors into the Log Explorer", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);
    const user = userEvent.setup();

    renderAt(`/alerts/${openWarning.id}`);

    const top = await screen.findByRole("region", { name: "Most frequent errors since 06:53" });
    expect(within(top).getAllByRole("listitem")).toHaveLength(5);
    await user.click(
      within(top).getByRole("link", { name: "Logs with “Circuit open for card-processor, request rejected”" }),
    );

    expect(await screen.findByRole("heading", { name: "Logs" })).toBeInTheDocument();
    const params = vi.mocked(api.searchLogs).mock.calls.at(-1)?.[0];
    expect(params?.get("text")).toBe("Circuit open for card-processor, request rejected");
    expect(params?.getAll("service")).toEqual(["payments-gateway"]);
    expect(params?.get("severity")).toBe("Error");
  });

  it("paints clear skies for a resolved alert and says how long it lasted", async () => {
    vi.mocked(api.alert).mockResolvedValue(resolvedCaution);

    const { container } = renderAt(`/alerts/${resolvedCaution.id}`);

    expect(await screen.findByRole("heading", { level: 1, name: "orders-api is back to 0.4% errors" })).toBeVisible();
    expect(screen.getByText("Resolved · was caution · lasted 14 min")).toBeVisible();
    expect(screen.getByText("Lasted").closest("div")).toHaveTextContent("14 min03:10–03:24 UTC");
    await waitFor(() => expect(container.querySelector(".shell")).toHaveClass("scene-clear"));
    expect(screen.getByText(/No channel was told about this alert/)).toBeVisible();
  });

  it("explains an unknown or purged alert", async () => {
    vi.mocked(api.alert).mockRejectedValue(new ApiError(404, "Not Found"));

    renderAt("/alerts/0193a2b4-0000-7000-8000-00000000ffff");

    expect(await screen.findByText(/Resolved alerts are kept for 90 days/)).toBeVisible();
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Alerts" })).toHaveAttribute("href", "/alerts");
    expect(screen.queryByRole("link", { name: "← Alerts" })).not.toBeInTheDocument();
  });

  it("says where the alert is in the bar above it, with the view toggle at the bar's end", async () => {
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    await screen.findByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" });
    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Alerts" })).toHaveAttribute("href", "/alerts");
    expect(within(trail).getByText("Error rate")).toHaveAttribute("aria-current", "page");
    expect(within(trail.parentElement as HTMLElement).getByRole("group", { name: "View" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "← Alerts" })).not.toBeInTheDocument();
  });

  it("is reached from Needs you on the home", async () => {
    vi.mocked(api.home).mockResolvedValue(stormSnapshot);
    vi.mocked(api.alert).mockResolvedValue(openWarning);
    const user = userEvent.setup();

    renderAt("/");

    const pane = await screen.findByRole("region", { name: "Needs you" });
    await user.click(within(pane).getByRole("link", { name: "Details" }));

    expect(await screen.findByRole("heading", { level: 1, name: "payments-gateway logs 21.6% errors" })).toBeVisible();
  });

  it("keeps to the headline, the facts, and the chart in the Simple view", async () => {
    localStorage.setItem("veyra.view", "simple");
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    expect(await screen.findByText("Peak")).toBeVisible();
    expect(screen.queryByRole("region", { name: "Timeline" })).not.toBeInTheDocument();
    expect(screen.getByText(/Timeline, messages, and the most frequent errors are in the Advanced view/)).toBeVisible();
  });
});

describe("AlertScreen guidance and help (VEY-60)", () => {
  beforeEach(() => {
    setRuleCatalog([integrityRule]);
  });

  it("says what an integrity alert means and how to fix it, with the check to run on its database", async () => {
    vi.mocked(api.alert).mockResolvedValue(openIntegrity);

    renderAt(`/alerts/${openIntegrity.id}`);

    const guide = await screen.findByRole("region", { name: "What it means and how to fix it" });
    expect(within(guide).getByText(/has not passed on VPower_2\.P for a while/)).toBeVisible();
    expect(within(guide).getAllByRole("listitem")).toHaveLength(2);
    expect(within(guide).getByLabelText("Run the check now, outside busy hours.")).toHaveTextContent(
      "DBCC CHECKDB (N'VPower_2.P') WITH NO_INFOMSGS;",
    );
    expect(within(guide).getByRole("button", { name: "Copy: Run the check now, outside busy hours." })).toBeVisible();
  });

  it("says how to fix it in the Simple view too", async () => {
    localStorage.setItem("veyra.view", "simple");
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    const guide = await screen.findByRole("region", { name: "What it means and how to fix it" });
    expect(within(guide).getByText(/payments-gateway logs a large share of errors/)).toBeVisible();
  });

  it("explains every tile behind its question mark, on a click or on hover", async () => {
    const user = userEvent.setup();
    vi.mocked(api.alert).mockResolvedValue(openIntegrity);

    const { container } = renderAt(`/alerts/${openIntegrity.id}`);

    // The panels below have question marks of their own; these are the five facts.
    await screen.findAllByRole("button", { name: /^What .* means$/ });
    const facts = within(container.querySelector("dl.alert-facts") as HTMLElement).getAllByRole("button", {
      name: /^What .* means$/,
    });
    expect(facts.map((button) => button.getAttribute("aria-label"))).toEqual([
      "What Now means",
      "What Peak means",
      "What Threshold means",
      "What Since means",
      "What Notified means",
    ]);

    await user.click(screen.getByRole("button", { name: "What Threshold means" }));
    expect(screen.getByRole("tooltip")).toHaveTextContent(/warning at 15 d/i);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

    await user.hover(screen.getByRole("button", { name: "What Notified means" }));
    expect(screen.getByRole("tooltip")).toHaveTextContent(/Notifications screen/);
    await user.unhover(screen.getByRole("button", { name: "What Notified means" }));
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("explains the chart and the panels below the facts", async () => {
    const user = userEvent.setup();
    vi.mocked(api.alert).mockResolvedValue(openWarning);

    renderAt(`/alerts/${openWarning.id}`);

    expect(await screen.findByRole("button", { name: "What Error rate means" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "What Most frequent errors means" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "What Timeline means" })).toBeInTheDocument();
    // The question marks sit beside the headings, so the panels keep their names.
    expect(screen.getByRole("region", { name: "Timeline" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "What Timeline means" }));
    expect(screen.getByRole("tooltip")).toHaveTextContent(/every change of the alert/i);
  });

  it("dates what happened on other days, and has no window for a rule without one", async () => {
    vi.mocked(api.alert).mockResolvedValue(openIntegrity);

    renderAt(`/alerts/${openIntegrity.id}`);

    expect(await screen.findByText(/^Open for 8 d \d+ h · warning$/)).toBeVisible();
    expect(
      screen.getByText("It crossed 8 d on Nov 17 at 02:05 and 15 d on Nov 24 at 02:01, in the local environment."),
    ).toBeVisible();
    expect(screen.queryByText(/Over the last 0 min/)).not.toBeInTheDocument();
    expect(screen.getByText("Now").closest("div")).toHaveTextContent("last checked at 11:01");
    const timeline = screen.getByRole("region", { name: "Timeline" });
    expect(within(timeline).getByText("Nov 17")).toBeVisible();
    expect(within(timeline).getByText("Nov 24")).toBeVisible();
  });
});

describe("AlertScreen errors a server's error rule counted (VEY-61)", () => {
  it("lists each error with its time, number, severity, and message, in both views", async () => {
    for (const view of ["advanced", "simple"]) {
      localStorage.setItem("veyra.view", view);
      vi.mocked(api.alert).mockResolvedValue(openSevereError);

      const { unmount } = renderAt(`/alerts/${openSevereError.id}`);

      const counted = await screen.findByRole("region", { name: "Errors it counted" });
      const error = within(counted).getByRole("listitem");
      expect(error).toHaveTextContent("13:11");
      expect(error).toHaveTextContent("Error 824 · severity 24");
      expect(error).toHaveTextContent("SQL Server detected a logical consistency-based I/O error: incorrect checksum.");
      expect(within(counted).getByRole("button", { name: "What Errors it counted means" })).toBeVisible();
      unmount();
    }
  });

  it("has no such list for other rules", async () => {
    vi.mocked(api.alert).mockResolvedValue(openIntegrity);

    renderAt(`/alerts/${openIntegrity.id}`);

    expect(await screen.findByRole("heading", { level: 1 })).toBeVisible();
    expect(screen.queryByRole("region", { name: "Errors it counted" })).not.toBeInTheDocument();
  });
});
