import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import { App } from "../app/App";
import { SessionProvider } from "../session/SessionProvider";
import { dana, people, tom, tomDetail } from "./testUsers";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      listUsers: vi.fn(),
      user: vi.fn(),
      createUser: vi.fn(),
      updateUser: vi.fn(),
      resetPassword: vi.fn(),
      removeSecondFactor: vi.fn(),
      endUserSession: vi.fn(),
      endUserSessions: vi.fn(),
      secondFactorPolicy: vi.fn(),
      setSecondFactorPolicy: vi.fn(),
    },
  };
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

// The counts and the lock depend on now: 07:40 UTC, while Tom is locked until 07:52.
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-11-10T07:40:00Z"));
  vi.mocked(api.currentSession).mockResolvedValue({
    id: dana.id,
    email: dana.email,
    displayName: dana.displayName,
    role: "Admin",
  });
  vi.mocked(api.listUsers).mockResolvedValue(people);
  vi.mocked(api.user).mockResolvedValue(tomDetail);
  vi.mocked(api.secondFactorPolicy).mockResolvedValue({ requiredFor: "Admins" });
  vi.mocked(api.setSecondFactorPolicy).mockImplementation((requiredFor) => Promise.resolve({ requiredFor }));
  vi.mocked(api.updateUser).mockResolvedValue({ ...tom, role: "Viewer" });
  vi.mocked(api.resetPassword).mockResolvedValue(undefined);
  vi.mocked(api.endUserSession).mockResolvedValue(undefined);
  vi.mocked(api.createUser).mockImplementation((input) =>
    Promise.resolve({ ...tom, id: "jonas", email: input.email, displayName: input.displayName, role: input.role }),
  );
});

afterEach(() => {
  vi.useRealTimers();
});

describe("UsersScreen", () => {
  it("sums up the accounts and shows a badge per person", async () => {
    renderAt("/users");

    const strip = await screen.findByRole("region", { name: "Security" });
    expect(strip).toHaveTextContent("5 of 8use a second factor");
    expect(strip).toHaveTextContent("6 of 8signed in within 30 days");
    expect(strip).toHaveTextContent("2 of 2Admins with a second factor");
    const grid = screen.getByRole("list", { name: "People" });
    expect(within(grid).getAllByRole("listitem")).toHaveLength(8);
    expect(within(grid).getByRole("button", { name: /Tom Okafor/ })).toHaveTextContent("Locked");
  });

  it("filters to the people without a second factor", async () => {
    const user = userEvent.setup();
    renderAt("/users");

    await user.click(await screen.findByRole("radio", { name: /No second factor/ }));

    const grid = screen.getByRole("list", { name: "People" });
    expect(
      within(grid)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      expect.stringContaining("Marco Rossi"),
      expect.stringContaining("Mei Chen"),
      expect.stringContaining("Sam Taylor"),
    ]);
  });

  it("opens a person with their sign-ins, flagging an address not seen before", async () => {
    const user = userEvent.setup();
    renderAt("/users");

    await user.click(await screen.findByRole("button", { name: /Tom Okafor/ }));
    const panel = await screen.findByRole("region", { name: "Tom Okafor" });
    expect(panel).toHaveTextContent("Locked until 07:52");
    await user.click(within(panel).getByRole("tab", { name: /Sign-ins/ }));

    const attempts = within(panel).getByRole("list", { name: "Latest sign-ins" });
    expect(within(attempts).getAllByRole("listitem")[0]).toHaveTextContent("Wrong password");
    expect(within(attempts).getAllByRole("listitem")[0]).toHaveTextContent("Address not seen before");
  });

  it("changes the role of the selected person", async () => {
    const user = userEvent.setup();
    renderAt("/users?person=tom");

    const panel = await screen.findByRole("region", { name: "Tom Okafor" });
    await user.click(within(panel).getByRole("radio", { name: /Viewer/ }));

    expect(api.updateUser).toHaveBeenCalledWith("tom", { role: "Viewer" });
  });

  it("signs out one session of the selected person", async () => {
    const user = userEvent.setup();
    renderAt("/users?person=tom&tab=sessions");

    const panel = await screen.findByRole("region", { name: "Tom Okafor" });
    const iphone = within(panel).getByRole("listitem", { name: "Safari on iPhone" });
    await user.click(within(iphone).getByRole("button", { name: "Sign out" }));

    expect(api.endUserSession).toHaveBeenCalledWith("tom", "s2");
  });

  it("resets a password with four made-up words", async () => {
    const user = userEvent.setup();
    renderAt("/users?person=tom");

    const panel = await screen.findByRole("region", { name: "Tom Okafor" });
    await user.click(within(panel).getByRole("button", { name: "Reset password" }));
    const dialog = screen.getByRole("dialog", { name: "Reset Tom's password" });
    const words = within(dialog).getByLabelText("New password").textContent ?? "";
    await user.click(within(dialog).getByRole("button", { name: "Reset password" }));

    expect(words.split("-")).toHaveLength(4);
    expect(api.resetPassword).toHaveBeenCalledWith("tom", words);
  });

  it("sets who must use a second factor", async () => {
    const user = userEvent.setup();
    renderAt("/users");

    await user.click(await screen.findByRole("radio", { name: "Everyone" }));

    expect(api.setSecondFactorPolicy).toHaveBeenCalledWith("Everyone");
    expect(screen.getByRole("region", { name: "Security" })).toHaveTextContent("2 active people");
  });

  it("says plainly when every Admin already has a second factor", async () => {
    vi.mocked(api.secondFactorPolicy).mockResolvedValue({ requiredFor: "Nobody" });
    renderAt("/users");

    expect(await screen.findByRole("region", { name: "Security" })).toHaveTextContent(
      "Nobody must. Every Admin has one already.",
    );
  });

  it("adds a person with a role, a team, and a first password", async () => {
    const user = userEvent.setup();
    renderAt("/users");

    await user.click(await screen.findByRole("button", { name: "+ Add user" }));
    const drawer = screen.getByRole("dialog", { name: "Add a user" });
    await user.type(within(drawer).getByLabelText("Email"), "jonas.weber@example.com");
    await user.type(within(drawer).getByLabelText("Name"), "Jonas Weber");
    await user.type(within(drawer).getByLabelText("Team"), "Support");
    await user.click(within(drawer).getByRole("radio", { name: /Editor/ }));
    const password = within(drawer).getByLabelText("First password").textContent ?? "";
    await user.click(within(drawer).getByRole("button", { name: "Add Jonas" }));

    expect(api.createUser).toHaveBeenCalledWith({
      email: "jonas.weber@example.com",
      displayName: "Jonas Weber",
      role: "Editor",
      password,
      team: "Support",
    });
  });
});
