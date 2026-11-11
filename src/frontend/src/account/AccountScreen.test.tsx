import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { lenaAccount } from "../users/testUsers";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      account: vi.fn(),
      changePassword: vi.fn(),
      startSecondFactor: vi.fn(),
      confirmSecondFactor: vi.fn(),
      newRecoveryCodes: vi.fn(),
      switchOffSecondFactor: vi.fn(),
      endOwnSession: vi.fn(),
      endOtherSessions: vi.fn(),
    },
  };
});

const codes = ["K7QF-29XM", "8D3N-PW4R", "TJ6V-1HQZ", "5MB2-C9LE"];

function renderAccount() {
  return render(
    <MemoryRouter initialEntries={["/account"]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-11-10T08:00:00Z"));
  vi.mocked(api.currentSession).mockResolvedValue({
    id: lenaAccount.id,
    email: lenaAccount.email,
    displayName: lenaAccount.displayName,
    role: "Editor",
  });
  vi.mocked(api.account).mockResolvedValue(lenaAccount);
  vi.mocked(api.changePassword).mockResolvedValue(undefined);
  vi.mocked(api.endOwnSession).mockResolvedValue(undefined);
  vi.mocked(api.endOtherSessions).mockResolvedValue({ ended: 1 });
  vi.mocked(api.newRecoveryCodes).mockResolvedValue({ recoveryCodes: codes });
  vi.mocked(api.switchOffSecondFactor).mockResolvedValue(undefined);
});

afterEach(() => {
  vi.useRealTimers();
});

describe("AccountScreen", () => {
  it("shows who you are, how far in you reach, and where you are signed in", async () => {
    renderAccount();

    expect(await screen.findByRole("heading", { level: 1, name: "Your account" })).toBeVisible();
    expect(await screen.findByText("Checkout")).toBeVisible();
    expect(screen.getByRole("img", { name: "Editor: the middle ring" })).toBeVisible();
    const sessions = screen.getByRole("list", { name: "Sessions" });
    expect(within(sessions).getByRole("listitem", { name: "Edge on Windows" })).toHaveTextContent("this one");
    expect(screen.getByRole("list", { name: "Recent sign-ins" })).toHaveTextContent("Wrong password");
    expect(screen.getByRole("region", { name: "Two-factor sign-in" })).toHaveTextContent("9 of 10 recovery codes left");
  });

  it("changes the password once both new ones match", async () => {
    const user = userEvent.setup();
    renderAccount();

    await user.type(await screen.findByLabelText("Current password"), "correct horse battery");
    await user.type(screen.getByLabelText("New password"), "lantern orbit maple quiet");
    await user.type(screen.getByLabelText("New password again"), "lantern orbit maple quie");
    expect(screen.getByRole("button", { name: "Change password" })).toBeDisabled();
    await user.type(screen.getByLabelText("New password again"), "t");
    await user.click(screen.getByRole("button", { name: "Change password" }));

    expect(api.changePassword).toHaveBeenCalledWith("correct horse battery", "lantern orbit maple quiet");
    expect(await screen.findByRole("status")).toHaveTextContent("Password changed");
  });

  it("signs out the phone, or every other browser", async () => {
    const user = userEvent.setup();
    renderAccount();

    const phone = await screen.findByRole("listitem", { name: "Chrome on Android" });
    await user.click(within(phone).getByRole("button", { name: "Sign out" }));
    await user.click(screen.getByRole("button", { name: "Sign out everywhere else" }));

    expect(api.endOwnSession).toHaveBeenCalledWith("phone");
    expect(api.endOtherSessions).toHaveBeenCalled();
  });

  it("makes new recovery codes after asking for the password", async () => {
    const user = userEvent.setup();
    renderAccount();

    await user.click(await screen.findByRole("button", { name: "New recovery codes" }));
    const dialog = screen.getByRole("dialog", { name: "New recovery codes" });
    await user.type(within(dialog).getByLabelText("Password"), "correct horse battery");
    await user.click(within(dialog).getByRole("button", { name: "Make new codes" }));

    expect(api.newRecoveryCodes).toHaveBeenCalledWith("correct horse battery");
    expect(await within(dialog).findByRole("list", { name: "Recovery codes" })).toHaveTextContent("K7QF-29XM");
  });

  it("sets up a second factor when it is off", async () => {
    vi.mocked(api.account).mockResolvedValue({ ...lenaAccount, secondFactor: null });
    vi.mocked(api.startSecondFactor).mockResolvedValue({
      secret: "JBSWY3DPEHPK3PXP",
      uri: "otpauth://totp/Veyra:lena?secret=JBSWY3DPEHPK3PXP&issuer=Veyra",
    });
    vi.mocked(api.confirmSecondFactor).mockResolvedValue({ recoveryCodes: codes });
    const user = userEvent.setup();
    renderAccount();

    await user.click(await screen.findByRole("button", { name: "Set up" }));
    const dialog = screen.getByRole("dialog", { name: "Set up two-factor sign-in" });
    await user.click(await within(dialog).findByRole("button", { name: "Next: enter a code" }));
    await user.type(within(dialog).getByLabelText("Code from the app"), "317904");
    await user.click(within(dialog).getByRole("button", { name: "Confirm" }));

    expect(api.confirmSecondFactor).toHaveBeenCalledWith("317904");
    expect(await within(dialog).findByRole("list", { name: "Recovery codes" })).toBeVisible();
  });

  it("does not offer to switch off a second factor an Admin requires", async () => {
    vi.mocked(api.account).mockResolvedValue({ ...lenaAccount, secondFactorRequired: true });
    renderAccount();

    const panel = await screen.findByRole("region", { name: "Two-factor sign-in" });

    expect(within(panel).queryByRole("button", { name: "Switch off" })).not.toBeInTheDocument();
    expect(panel).toHaveTextContent("Your administrator requires it");
  });
});
