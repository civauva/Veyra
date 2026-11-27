import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../../../api/client";
import { api } from "../../../api/client";
import type { LookChoice } from "../../../api/types";
import { SessionContext, type SessionValue } from "../../../session/SessionContext";
import { listingOf, settingsPage } from "../../../settings/testSettings";
import { ConsoleSettings } from "./ConsoleSettings";

vi.mock("../../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      settings: vi.fn(),
      settingsListing: vi.fn(),
      reviewSettings: vi.fn(),
      saveSettings: vi.fn(),
      agentsSummary: vi.fn(),
      clearTelemetry: vi.fn(),
    },
  };
});

function renderAt(path: string, look: LookChoice = { chosen: null, default: "console", mayChoose: true }) {
  const setLook = vi.fn().mockResolvedValue(undefined);
  const value = {
    state: {
      status: "signedIn",
      session: { id: "1", email: "a@example.com", displayName: "Ana", role: "Admin", timeZone: "UTC", look },
    },
    setLook,
  } as unknown as SessionValue;

  render(
    <SessionContext value={value}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/settings" element={<ConsoleSettings />} />
        </Routes>
      </MemoryRouter>
    </SessionContext>,
  );

  return setLook;
}

beforeEach(() => {
  localStorage.setItem("veyra.view", "advanced");
  vi.mocked(api.settings).mockResolvedValue(settingsPage);
  vi.mocked(api.settingsListing).mockImplementation((choices) => Promise.resolve(listingOf(settingsPage, choices)));
  vi.mocked(api.agentsSummary).mockResolvedValue({
    sqlServers: 0,
    postgresServers: 0,
    needLook: 0,
    agents: 0,
    agentsOnline: 0,
  });
  vi.mocked(api.reviewSettings).mockResolvedValue({
    problems: [],
    notes: [],
    changes: [{ key: "Veyra:Storage:LogRetentionDays", from: "14", to: "30" }],
    confirmationRequired: false,
  });
  vi.mocked(api.saveSettings).mockResolvedValue(settingsPage);
});

describe("ConsoleSettings", () => {
  it("ConsoleSettings_Section_ShowsTheSectionsAndEachSettingAsARow", async () => {
    renderAt("/settings");

    const sections = await screen.findByRole("navigation", { name: "Settings sections" });
    expect(within(sections).getByRole("link", { name: /Data and retention/ })).toHaveAttribute("aria-current", "page");

    const logs = await screen.findByRole("region", { name: "Logs" });
    expect(within(logs).getByRole("textbox", { name: "Logs, Keep" })).toHaveValue("14");
    expect(within(logs).getByText("Default 14 days, from VEYRA_LOG_RETENTION_DAYS")).toBeVisible();
  });

  it("ConsoleSettings_Edit_ShowsTheSaveBarThatDiscardsOrReviewsAndSaves", async () => {
    const user = userEvent.setup();
    renderAt("/settings");

    const keep = await screen.findByRole("textbox", { name: "Logs, Keep" });
    await user.clear(keep);
    await user.type(keep, "30");

    const bar = screen.getByRole("region", { name: "Unsaved changes" });
    expect(within(bar).getByText("1 change")).toBeVisible();
    expect(within(bar).getByText("Logs, Keep: 14 days to 30 days")).toBeInTheDocument();

    await user.click(within(bar).getByRole("button", { name: "Review and save" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^Save/ }));

    await waitFor(() =>
      expect(api.saveSettings).toHaveBeenCalledWith(
        settingsPage.version,
        [{ key: "Veyra:Storage:LogRetentionDays", value: "30" }],
        false,
        null,
      ),
    );
    expect(await screen.findByText("Saved. Veyra uses the new values now.")).toBeVisible();
    expect(screen.queryByRole("region", { name: "Unsaved changes" })).not.toBeInTheDocument();
  });

  it("ConsoleSettings_Discard_ThrowsTheEditsAway", async () => {
    const user = userEvent.setup();
    renderAt("/settings");

    const keep = await screen.findByRole("textbox", { name: "Logs, Keep" });
    await user.type(keep, "0");
    await user.click(screen.getByRole("button", { name: "Discard" }));

    expect(screen.queryByRole("region", { name: "Unsaved changes" })).not.toBeInTheDocument();
    expect(keep).toHaveValue("14");
  });

  it("ConsoleSettings_Appearance_OffersEveryLookAsACardAndTheDefaultsForEveryone", async () => {
    const user = userEvent.setup();
    const setLook = renderAt("/settings?section=Appearance");

    const looks = await screen.findByRole("radiogroup", { name: "Look" });
    expect(within(looks).getAllByRole("radio")).toHaveLength(2);
    expect(within(looks).getByRole("radio", { name: /Console/ })).toBeChecked();
    expect(within(looks).getByText("Default")).toBeVisible();

    await user.click(within(looks).getByRole("radio", { name: /Forecast/ }));
    expect(setLook).toHaveBeenCalledWith("forecast");

    const everyone = screen.getByRole("region", { name: "Look for everyone" });
    expect(within(everyone).getByRole("combobox", { name: /Default look/ })).toBeVisible();
    expect(within(everyone).getByRole("switch", { name: /Let people choose their look/ })).toBeChecked();
  });

  it("ConsoleSettings_Search_IsSentToTheServer", async () => {
    const user = userEvent.setup();
    renderAt("/settings");

    await user.type(await screen.findByRole("searchbox", { name: "Find a setting" }), "smtp");

    await waitFor(() =>
      expect(api.settingsListing).toHaveBeenLastCalledWith(
        { search: "smtp", section: "Data", view: "advanced" },
        expect.anything(),
      ),
    );
  });
});
