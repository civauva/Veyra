import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import type { SettingsReview } from "../api/types";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { settingsHistory, settingsPage } from "./testSettings";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
      settings: vi.fn(),
      reviewSettings: vi.fn(),
      saveSettings: vi.fn(),
      settingsHistory: vi.fn(),
      revertSetting: vi.fn(),
      exportSettings: vi.fn(),
      importSettings: vi.fn(),
      sqlServers: vi.fn(),
      postgresServers: vi.fn(),
      agents: vi.fn(),
      deleteSqlServer: vi.fn(),
      createSqlServer: vi.fn(),
      updateSqlServer: vi.fn(),
      clearTelemetry: vi.fn(),
    },
  };
});

const admin = { id: "1", email: "ana@example.com", displayName: "Ana Admin", role: "Admin" as const, timeZone: null };

const quietReview: SettingsReview = {
  problems: [],
  notes: [],
  changes: [{ key: "Veyra:Alerting:Latency:WarningSeconds", from: "2", to: "1.5" }],
  confirmationRequired: false,
};

function renderAt(path = "/settings") {
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
  vi.mocked(api.currentSession).mockResolvedValue(admin);
  vi.mocked(api.settings).mockResolvedValue(settingsPage);
  vi.mocked(api.settingsHistory).mockResolvedValue(settingsHistory);
  vi.mocked(api.reviewSettings).mockResolvedValue(quietReview);
  vi.mocked(api.saveSettings).mockResolvedValue(settingsPage);
  vi.mocked(api.sqlServers).mockResolvedValue([]);
  vi.mocked(api.postgresServers).mockResolvedValue([]);
  vi.mocked(api.agents).mockResolvedValue([]);
});

describe("SettingsScreen", () => {
  it("starts in the Simple view, which leaves out advanced settings and sections", async () => {
    renderAt("/settings?section=Rules");

    expect(await screen.findByRole("textbox", { name: "Error rate, Caution from" })).toBeVisible();
    expect(screen.queryByRole("textbox", { name: /Check the rules every/ })).not.toBeInTheDocument();
    const sections = screen.getByRole("navigation", { name: "Settings sections" });
    expect(within(sections).queryByRole("link", { name: /Ingestion/ })).not.toBeInTheDocument();
    expect(within(sections).queryByRole("link", { name: /System/ })).not.toBeInTheDocument();
  });

  it("shows every setting in the Advanced view, and remembers the choice", async () => {
    const user = userEvent.setup();
    const { unmount } = renderAt("/settings?section=Rules");

    await user.click(await screen.findByRole("button", { name: "Advanced" }));

    expect(screen.getByRole("textbox", { name: /Check the rules every/ })).toHaveValue("30");
    expect(
      within(screen.getByRole("navigation", { name: "Settings sections" })).getByRole("link", { name: /Ingestion/ }),
    ).toBeVisible();
    unmount();

    renderAt("/settings?section=Rules");
    expect(await screen.findByRole("textbox", { name: /Check the rules every/ })).toBeVisible();
    expect(screen.getByRole("button", { name: "Advanced" })).toHaveAttribute("aria-pressed", "true");
  });

  it("finds an advanced setting from the Simple view, and says that it is one", async () => {
    const user = userEvent.setup();
    renderAt();

    await user.type(await screen.findByRole("searchbox", { name: "Find a setting" }), "rows per request");

    const field = screen.getByRole("textbox", { name: "Paging, Rows per request at most" });
    expect(field).toHaveValue("1000");
    expect(within(screen.getByRole("region", { name: "Paging" })).getByText("Advanced")).toBeVisible();
  });

  it("opens an advanced-only section linked from elsewhere, even in the Simple view", async () => {
    renderAt("/settings?section=Ingestion");

    expect(await screen.findByRole("textbox", { name: "Queues, Log records waiting" })).toHaveValue("10000");
  });

  it("lists the sections and opens the first, with each value, its default, and where that comes from", async () => {
    renderAt();

    expect(await screen.findByRole("heading", { level: 1, name: "Settings" })).toBeVisible();
    const sections = await screen.findByRole("navigation", { name: "Settings sections" });
    expect(
      within(sections)
        .getAllByRole("link")
        .map((link) => link.textContent),
    ).toEqual([
      expect.stringContaining("Data and retention"),
      expect.stringContaining("Alert rules"),
      expect.stringContaining("Database monitoring"),
      expect.stringContaining("Agents"),
      expect.stringContaining("Notifications and email"),
      expect.stringContaining("Sign-in and security"),
    ]);
    expect(screen.getByRole("button", { name: "Simple" })).toHaveAttribute("aria-pressed", "true");

    expect(screen.getByRole("textbox", { name: "Logs, Keep" })).toHaveValue("14");
    expect(screen.getByText("Default 14 days, from VEYRA_LOG_RETENTION_DAYS")).toBeVisible();
  });

  it("shows a section from the address, and a value someone saved with who saved it", async () => {
    localStorage.setItem("veyra.view", "advanced");
    renderAt("/settings?section=Rules");

    expect(await screen.findByRole("textbox", { name: "Error rate, Caution from" })).toHaveValue("1");
    expect(screen.getByRole("switch", { name: "Error rate, On" })).toBeChecked();
    expect(screen.getByRole("textbox", { name: /Check the rules every/ })).toHaveValue("30");
    const latency = screen.getByRole("region", { name: "Latency" });
    expect(within(latency).getByText(/Saved by Lena Adler/)).toBeVisible();
    expect(within(latency).getByRole("button", { name: "Reset Warning from to 3 s" })).toBeVisible();
  });

  it("collects a change, reviews it, and saves it in configuration form", async () => {
    const user = userEvent.setup();
    renderAt("/settings?section=Rules");

    const field = await screen.findByRole("textbox", { name: "Error rate, Caution from" });
    await user.clear(field);
    await user.type(field, "2");

    const bar = screen.getByRole("region", { name: "Unsaved changes" });
    expect(within(bar).getByText("1 unsaved change")).toBeVisible();
    expect(within(bar).getByText("Error rate, Caution from: 1 % to 2 %")).toBeVisible();

    vi.mocked(api.reviewSettings).mockResolvedValue({
      ...quietReview,
      changes: [{ key: "Veyra:Alerting:ErrorRate:CautionRatio", from: "0.01", to: "0.02" }],
    });
    await user.click(within(bar).getByRole("button", { name: "Review and save" }));

    expect(api.reviewSettings).toHaveBeenCalledWith(7, [
      { key: "Veyra:Alerting:ErrorRate:CautionRatio", value: "0.02" },
    ]);
    const dialog = await screen.findByRole("dialog", { name: "Review 1 change" });
    expect(within(dialog).getByText("1 % → 2 %")).toBeVisible();
    await user.click(within(dialog).getByRole("button", { name: "Save 1 change" }));

    expect(api.saveSettings).toHaveBeenCalledWith(
      7,
      [{ key: "Veyra:Alerting:ErrorRate:CautionRatio", value: "0.02" }],
      false,
      null,
    );
    await waitFor(() => expect(screen.queryByRole("region", { name: "Unsaved changes" })).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent("Saved. Veyra uses the new values now.");
  });

  it("asks for a confirmation before a change that deletes data", async () => {
    const user = userEvent.setup();
    renderAt();
    const field = await screen.findByRole("textbox", { name: "Logs, Keep" });
    await user.clear(field);
    await user.type(field, "7");
    vi.mocked(api.reviewSettings).mockResolvedValue({
      problems: [],
      notes: [
        {
          key: "Veyra:Storage:LogRetentionDays",
          text: "Deletes 7 days of logs, 2.3 GB, at the next hourly clean-up. Deleted logs cannot be brought back.",
          risky: true,
        },
      ],
      changes: [{ key: "Veyra:Storage:LogRetentionDays", from: "14", to: "7" }],
      confirmationRequired: true,
    });

    await user.click(screen.getByRole("button", { name: "Review and save" }));

    const dialog = await screen.findByRole("dialog", { name: "Review 1 change" });
    expect(within(dialog).getByText(/Deletes 7 days of logs, 2.3 GB/)).toBeVisible();
    const save = within(dialog).getByRole("button", { name: "Save 1 change" });
    expect(save).toBeDisabled();
    await user.click(within(dialog).getByRole("checkbox", { name: /I understand/ }));
    await user.click(save);
    expect(api.saveSettings).toHaveBeenCalledWith(
      7,
      [{ key: "Veyra:Storage:LogRetentionDays", value: "7" }],
      true,
      null,
    );
  });

  it("shows a problem from the review next to its field and keeps the change", async () => {
    const user = userEvent.setup();
    renderAt("/settings?section=Rules");
    const field = await screen.findByRole("textbox", { name: "Error rate, Caution from" });
    await user.clear(field);
    await user.type(field, "20");
    vi.mocked(api.reviewSettings).mockResolvedValue({
      problems: [
        {
          key: "Veyra:Alerting:ErrorRate:WarningRatio",
          message: "Veyra:Alerting:ErrorRate:WarningRatio must not be below the caution level.",
        },
      ],
      notes: [],
      changes: [],
      confirmationRequired: false,
    });

    await user.click(screen.getByRole("button", { name: "Review and save" }));

    const warning = screen.getByRole("textbox", { name: "Error rate, Warning from" });
    expect(await screen.findByText("Must not be below the caution level.")).toBeVisible();
    expect(warning).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(field).toHaveValue("20");
  });

  it("finds a setting in any section by what is typed in the search", async () => {
    const user = userEvent.setup();
    renderAt();

    await user.type(await screen.findByRole("searchbox", { name: "Find a setting" }), "smtp server");

    expect(screen.getByRole("textbox", { name: "Email server, Server" })).toHaveValue("smtp.example.com");
    expect(screen.queryByRole("textbox", { name: "Logs, Keep" })).not.toBeInTheDocument();
  });

  it("never shows a secret, and sends a new one only when it is changed", async () => {
    const user = userEvent.setup();
    renderAt("/settings?section=Email");

    const card = await screen.findByRole("region", { name: "Email server" });
    expect(within(card).getByText("Set · stored encrypted")).toBeVisible();
    await user.click(within(card).getByRole("button", { name: "Change Password" }));
    await user.type(within(card).getByLabelText("Email server, Password"), "a new long password");
    await user.click(screen.getByRole("button", { name: "Review and save" }));

    expect(api.reviewSettings).toHaveBeenCalledWith(7, [
      { key: "Veyra:Alerting:Notifications:Smtp:Password", value: "a new long password" },
    ]);
  });

  it("resets a saved value to its default", async () => {
    const user = userEvent.setup();
    renderAt("/settings?section=Rules");

    await user.click(await screen.findByRole("button", { name: "Reset Warning from to 3 s" }));
    await user.click(screen.getByRole("button", { name: "Review and save" }));

    expect(api.reviewSettings).toHaveBeenCalledWith(7, [{ key: "Veyra:Alerting:Latency:WarningSeconds", value: null }]);
  });

  it("lists the history and reverts a change", async () => {
    const user = userEvent.setup();
    vi.mocked(api.revertSetting).mockResolvedValue(settingsPage);
    renderAt();

    await user.click(await screen.findByRole("button", { name: "History" }));

    const history = await screen.findByRole("complementary", { name: "Settings history" });
    expect(within(history).getByText("3 s → 2 s")).toBeVisible();
    expect(within(history).getByText("Changed, hidden")).toBeVisible();
    expect(within(history).getByText(/From veyra-settings-staging.json/)).toBeVisible();
    await user.click(within(history).getByRole("button", { name: "Revert Warning from" }));
    expect(api.revertSetting).toHaveBeenCalledWith(12, 7, false);
  });

  it("lists changes to monitored systems by the fields they touched, without values or revert", async () => {
    const user = userEvent.setup();
    renderAt();

    await user.click(await screen.findByRole("button", { name: "History" }));

    const history = await screen.findByRole("complementary", { name: "Settings history" });
    expect(within(history).getByText("SQL Server · sql-prod-01")).toBeVisible();
    expect(within(history).getByText("Changed port, password")).toBeVisible();
    expect(within(history).getByText("Agent · Company network")).toBeVisible();
    expect(within(history).getByText("Added")).toBeVisible();
    expect(within(history).queryByRole("button", { name: "Revert sql-prod-01" })).toBeNull();
  });

  it("says when someone else saved first, and loads the latest values", async () => {
    const user = userEvent.setup();
    renderAt("/settings?section=Rules");
    const field = await screen.findByRole("textbox", { name: "Error rate, Caution from" });
    await user.clear(field);
    await user.type(field, "2");
    await user.click(screen.getByRole("button", { name: "Review and save" }));
    vi.mocked(api.saveSettings).mockRejectedValue(new ApiError(409, "Someone else saved settings in the meantime."));

    await user.click(await screen.findByRole("button", { name: "Save 1 change" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else saved settings in the meantime.");
    await user.click(screen.getByRole("button", { name: "Load the latest values" }));
    await waitFor(() => expect(api.settings).toHaveBeenCalledTimes(2));
  });

  it("shows what is set where Veyra is installed, read-only", async () => {
    const user = userEvent.setup();
    localStorage.setItem("veyra.view", "advanced");
    renderAt();

    await user.click(await screen.findByRole("link", { name: /System/ }));

    const system = screen.getByRole("region", { name: "System" });
    expect(within(system).getByText("veyra on postgres:5432")).toBeVisible();
    expect(within(system).queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("clears all telemetry from the Data section only once the site's name is typed", async () => {
    const user = userEvent.setup();
    vi.mocked(api.clearTelemetry).mockResolvedValue(undefined);
    renderAt("/settings?section=Data");

    const panel = await screen.findByRole("region", { name: "Clear all telemetry" });
    const button = within(panel).getByRole("button", { name: "Clear all telemetry" });
    expect(button).toBeDisabled();

    await user.type(within(panel).getByRole("textbox"), "not the site");
    expect(button).toBeDisabled();

    await user.clear(within(panel).getByRole("textbox"));
    await user.type(within(panel).getByRole("textbox"), window.location.hostname);
    await user.click(button);

    expect(api.clearTelemetry).toHaveBeenCalledWith(window.location.hostname);
    expect(await within(panel).findByRole("status")).toHaveTextContent("Cleared.");
  });
});
