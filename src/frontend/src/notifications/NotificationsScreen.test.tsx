import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { channels, emailServer, onCall, opsAlerts } from "./testChannels";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      listChannels: vi.fn(),
      createChannel: vi.fn(),
      updateChannel: vi.fn(),
      deleteChannel: vi.fn(),
      testChannel: vi.fn(),
      testDraft: vi.fn(),
    },
  };
});

const admin = { id: "1", email: "ana@example.com", displayName: "Ana Admin", role: "Admin" as const };

function renderScreen() {
  return render(
    <MemoryRouter initialEntries={["/notifications"]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(api.currentSession).mockResolvedValue(admin);
  vi.mocked(api.listChannels).mockResolvedValue({ emailServer, channels });
});

describe("NotificationsScreen", () => {
  it("shows the email server and one card per channel, the failing one with its reason", async () => {
    renderScreen();

    expect(await screen.findByRole("heading", { level: 1, name: "Notifications" })).toBeVisible();
    expect(await screen.findByText("smtp.example.com:587")).toBeVisible();

    const bot = screen.getByRole("article", { name: "Incident bot" });
    expect(bot).toHaveClass("failing");
    expect(within(bot).getByText(/Last try failed at 07:04\./)).toBeVisible();
    expect(within(bot).getByText(/Timed out after 10 s\. Veyra tries again at 07:12\./)).toBeVisible();
    expect(within(bot).getByText("14 days · 2 sent, 3 failed")).toBeVisible();

    const ops = screen.getByRole("article", { name: "#ops-alerts" });
    expect(within(ops).getByText("Webhook · https://hooks.slack.com/services/T0…")).toBeVisible();
    expect(within(ops).getByText("Last 07:01 · 204")).toBeVisible();
    expect(within(ops).getByRole("switch", { name: "#ops-alerts switched on" })).toBeChecked();

    expect(within(screen.getByRole("article", { name: "Weekend team" })).getByText(/Switched off/)).toBeVisible();
  });

  it("adds a webhook from the side panel", async () => {
    const user = userEvent.setup();
    vi.mocked(api.createChannel).mockResolvedValue(opsAlerts);
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "+ Add channel" }));
    const panel = screen.getByRole("dialog", { name: "Add a channel" });
    expect(within(panel).getByLabelText("Name")).toHaveFocus();
    await user.type(within(panel).getByLabelText("Name"), "#ops-alerts");
    await user.type(within(panel).getByLabelText("URL"), "https://hooks.slack.com/services/T0A1/B0C2/x9Y");
    await user.click(within(panel).getByLabelText("Caution"));
    await user.click(within(panel).getByRole("button", { name: "Save channel" }));

    expect(api.createChannel).toHaveBeenCalledWith({
      name: "#ops-alerts",
      kind: "Webhook",
      enabled: true,
      sendWarning: true,
      sendCaution: false,
      sendResolved: true,
      webhookUrl: "https://hooks.slack.com/services/T0A1/B0C2/x9Y",
      emailRecipients: null,
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(api.listChannels).toHaveBeenCalledTimes(2);
  });

  it("adds an email channel with one address per line", async () => {
    const user = userEvent.setup();
    vi.mocked(api.createChannel).mockResolvedValue(onCall);
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "+ Add channel" }));
    const panel = screen.getByRole("dialog");
    await user.click(within(panel).getByLabelText("Email"));
    await user.type(within(panel).getByLabelText("Name"), "On-call");
    await user.type(within(panel).getByLabelText("Recipients"), "oncall@example.com{enter}lead@example.com");
    await user.click(within(panel).getByRole("button", { name: "Save channel" }));

    expect(api.createChannel).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: "Email",
        webhookUrl: null,
        emailRecipients: ["oncall@example.com", "lead@example.com"],
      }),
    );
  });

  it("keeps the stored webhook URL when editing without typing a new one", async () => {
    const user = userEvent.setup();
    vi.mocked(api.updateChannel).mockResolvedValue(opsAlerts);
    renderScreen();

    const ops = await screen.findByRole("article", { name: "#ops-alerts" });
    await user.click(within(ops).getByRole("button", { name: "Edit" }));
    const panel = screen.getByRole("dialog", { name: "Change #ops-alerts" });
    expect(within(panel).getByLabelText("URL")).toHaveAttribute("placeholder", "https://hooks.slack.com/services/T0…");
    await user.clear(within(panel).getByLabelText("Name"));
    await user.type(within(panel).getByLabelText("Name"), "#ops");
    await user.click(within(panel).getByRole("button", { name: "Save changes" }));

    expect(api.updateChannel).toHaveBeenCalledWith("c1", expect.objectContaining({ name: "#ops", webhookUrl: null }));
  });

  it("shows why saving failed and keeps the panel open", async () => {
    const user = userEvent.setup();
    vi.mocked(api.createChannel).mockRejectedValue(new ApiError(409, "Another channel already has this name."));
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "+ Add channel" }));
    const panel = screen.getByRole("dialog");
    await user.type(within(panel).getByLabelText("Name"), "#ops-alerts");
    await user.type(within(panel).getByLabelText("URL"), "https://hooks.example.com/x");
    await user.click(within(panel).getByRole("button", { name: "Save channel" }));

    expect(await within(panel).findByRole("alert")).toHaveTextContent("Another channel already has this name.");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("sends a test before saving and tells how it went", async () => {
    const user = userEvent.setup();
    vi.mocked(api.testDraft).mockResolvedValue({ succeeded: false, status: 404, error: "HTTP 404 Not Found" });
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "+ Add channel" }));
    const panel = screen.getByRole("dialog");
    await user.type(within(panel).getByLabelText("Name"), "hook");
    await user.type(within(panel).getByLabelText("URL"), "https://hooks.example.com/x");
    await user.click(within(panel).getByRole("button", { name: "Send test" }));

    expect(await within(panel).findByRole("status")).toHaveTextContent("Test failed: HTTP 404 Not Found.");
    expect(api.testDraft).toHaveBeenCalledWith(expect.objectContaining({ name: "hook" }), null);
  });

  it("closes the side panel with Escape", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "+ Add channel" }));
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("switches a channel off without touching its URL", async () => {
    const user = userEvent.setup();
    vi.mocked(api.updateChannel).mockResolvedValue(opsAlerts);
    renderScreen();

    await user.click(await screen.findByRole("switch", { name: "#ops-alerts switched on" }));

    expect(api.updateChannel).toHaveBeenCalledWith(
      "c1",
      expect.objectContaining({ enabled: false, webhookUrl: null, name: "#ops-alerts" }),
    );
  });

  it("sends a test from a card", async () => {
    const user = userEvent.setup();
    vi.mocked(api.testChannel).mockResolvedValue({ succeeded: true, status: 204, error: null });
    renderScreen();

    const ops = await screen.findByRole("article", { name: "#ops-alerts" });
    await user.click(within(ops).getByRole("button", { name: "Send test" }));

    expect(await within(ops).findByRole("status")).toHaveTextContent("Test delivered (204).");
  });

  it("deletes a channel only after confirming", async () => {
    const user = userEvent.setup();
    vi.mocked(api.deleteChannel).mockResolvedValue(undefined);
    renderScreen();

    const bot = await screen.findByRole("article", { name: "Incident bot" });
    await user.click(within(bot).getByRole("button", { name: "Delete" }));
    expect(api.deleteChannel).not.toHaveBeenCalled();
    await user.click(within(bot).getByRole("button", { name: "Yes, delete" }));

    expect(api.deleteChannel).toHaveBeenCalledWith("c3");
  });

  it("warns when email channels have no server to send through", async () => {
    vi.mocked(api.listChannels).mockResolvedValue({
      emailServer: { configured: false, host: null, port: 587, security: "Auto", from: null },
      channels,
    });
    renderScreen();

    expect(await screen.findByText("No email server")).toBeVisible();
    const onCall = screen.getByRole("article", { name: "On-call" });
    expect(within(onCall).getByText(/this channel cannot send/)).toBeVisible();
  });

  it("is not offered to viewers", async () => {
    vi.mocked(api.currentSession).mockResolvedValue({ ...admin, role: "Viewer" });
    vi.mocked(api.home).mockRejectedValue(new ApiError(500, "not needed"));
    renderScreen();

    expect(await screen.findByRole("navigation", { name: "Main" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Notifications" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Notifications" })).not.toBeInTheDocument();
  });
});
