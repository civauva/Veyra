import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { App } from "../app/App";
import { clearSnapshot } from "../home/testSnapshots";
import { SessionProvider } from "../session/SessionProvider";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signIn: vi.fn(),
      signInWithCode: vi.fn(),
      startRequiredSetup: vi.fn(),
      confirmRequiredSetup: vi.fn(),
      signOut: vi.fn(),
      home: vi.fn(),
    },
  };
});

const lena = { id: "2", email: "lena@example.com", displayName: "Lena Hoffmann", role: "Editor" as const };
const codes = ["K7QF-29XM", "8D3N-PW4R", "TJ6V-1HQZ", "5MB2-C9LE", "R4XW-7GKA"];

function renderSignIn() {
  return render(
    <MemoryRouter initialEntries={["/"]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

async function typePassword(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText("Email"), "lena@example.com");
  await user.type(screen.getByLabelText("Password"), "correct horse battery");
  await user.click(screen.getByRole("button", { name: "Sign in" }));
}

beforeEach(() => {
  vi.mocked(api.currentSession).mockResolvedValue(null);
  vi.mocked(api.setupStatus).mockResolvedValue({ required: false });
  vi.mocked(api.home).mockResolvedValue(clearSnapshot);
});

describe("Signing in with a second factor", () => {
  it("asks for the code after the password and signs in with it", async () => {
    vi.mocked(api.signIn).mockResolvedValue({ next: "code" });
    vi.mocked(api.signInWithCode).mockResolvedValue(lena);
    const user = userEvent.setup();
    renderSignIn();

    await typePassword(user);
    expect(await screen.findByRole("heading", { name: "Enter your code" })).toBeVisible();
    await user.type(screen.getByLabelText("Code from the app"), "482093");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(api.signInWithCode).toHaveBeenCalledWith("482093");
    expect(await screen.findByRole("navigation", { name: "Main" })).toBeVisible();
  });

  it("takes a recovery code instead of the app's code", async () => {
    vi.mocked(api.signIn).mockResolvedValue({ next: "code" });
    vi.mocked(api.signInWithCode).mockResolvedValue(lena);
    const user = userEvent.setup();
    renderSignIn();

    await typePassword(user);
    await user.click(await screen.findByRole("button", { name: "Use a recovery code instead" }));
    await user.type(screen.getByLabelText("Recovery code"), "K7QF-29XM");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(api.signInWithCode).toHaveBeenCalledWith("K7QF-29XM");
  });

  it("says so when the code is wrong and lets the person try again", async () => {
    vi.mocked(api.signIn).mockResolvedValue({ next: "code" });
    vi.mocked(api.signInWithCode).mockRejectedValue(new ApiError(401, "The code is wrong or was used already."));
    const user = userEvent.setup();
    renderSignIn();

    await typePassword(user);
    await user.type(await screen.findByLabelText("Code from the app"), "000000");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The code is wrong or was used already.");
    expect(screen.getByLabelText("Code from the app")).toHaveValue("");
  });

  it("sets up the required second factor right after the password, then signs in", async () => {
    vi.mocked(api.signIn).mockResolvedValue({ next: "setup" });
    vi.mocked(api.startRequiredSetup).mockResolvedValue({
      secret: "JBSWY3DPEHPK3PXP",
      uri: "otpauth://totp/Veyra:lena%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Veyra",
    });
    vi.mocked(api.confirmRequiredSetup).mockResolvedValue({ session: lena, recoveryCodes: codes });
    const user = userEvent.setup();
    renderSignIn();

    await typePassword(user);
    expect(await screen.findByRole("heading", { name: "One more step" })).toBeVisible();
    expect(await screen.findByRole("img", { name: "QR code for lena@example.com" })).toBeVisible();
    expect(screen.getByText("JBSW Y3DP EHPK 3PXP")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Next: enter a code" }));
    await user.type(screen.getByLabelText("Code from the app"), "317904");
    await user.click(screen.getByRole("button", { name: "Confirm" }));

    expect(api.confirmRequiredSetup).toHaveBeenCalledWith("317904");
    const list = await screen.findByRole("list", { name: "Recovery codes" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(5);
    expect(screen.getByRole("button", { name: "Done" })).toBeDisabled();
    await user.click(screen.getByLabelText("I have stored these codes somewhere safe"));
    await user.click(screen.getByRole("button", { name: "Done" }));

    expect(await screen.findByRole("navigation", { name: "Main" })).toBeVisible();
  });
});
