import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import type { LookChoice } from "../api/types";
import { SessionContext, type SessionValue } from "../session/SessionContext";
import { LookPanel } from "./LookPanel";

/** The panel for a person whose look choice is as given. */
function renderWith(look: LookChoice, setLook = vi.fn().mockResolvedValue(undefined)) {
  const value = {
    state: {
      status: "signedIn",
      session: { id: "1", email: "a@example.com", displayName: "Ana", role: "Viewer", timeZone: null, look },
    },
    setLook,
  } as unknown as SessionValue;

  render(
    <SessionContext value={value}>
      <LookPanel />
    </SessionContext>,
  );

  return setLook;
}

describe("LookPanel", () => {
  it("LookPanel_NoChoiceYet_MarksTheDefaultAsTheLookInUse", () => {
    renderWith({ chosen: null, default: "forecast", mayChoose: true });

    expect(screen.getByRole("radio", { name: /Forecast/ })).toBeChecked();
    expect(screen.getByRole("radio", { name: /Console/ })).not.toBeChecked();
    expect(screen.queryByRole("button", { name: /Use the default/ })).not.toBeInTheDocument();
  });

  it("LookPanel_PickConsole_KeepsItOnTheAccount", async () => {
    const setLook = renderWith({ chosen: null, default: "forecast", mayChoose: true });

    await userEvent.click(screen.getByRole("radio", { name: /Console/ }));

    await waitFor(() => expect(setLook).toHaveBeenCalledWith("console"));
  });

  it("LookPanel_OwnChoice_CanGoBackToTheDefault", async () => {
    const setLook = renderWith({ chosen: "console", default: "forecast", mayChoose: true });

    expect(screen.getByRole("radio", { name: /Console/ })).toBeChecked();
    await userEvent.click(screen.getByRole("button", { name: "Use the default, Forecast" }));

    await waitFor(() => expect(setLook).toHaveBeenCalledWith(null));
  });

  it("LookPanel_ChoosingIsOff_SaysSoAndShowsTheDefault", () => {
    renderWith({ chosen: "forecast", default: "console", mayChoose: false });

    expect(screen.getByText("An Admin has set one look for everyone: Console.")).toBeVisible();
    expect(screen.getByRole("radio", { name: /Console/ })).toBeChecked();
    expect(screen.getByRole("radio", { name: /Forecast/ })).toBeDisabled();
  });
});
