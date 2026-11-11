import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api, ApiError } from "../api/client";
import { clearSnapshot } from "../home/testSnapshots";
import { SessionProvider } from "../session/SessionProvider";
import { App } from "./App";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return {
    ...original,
    api: {
      setupStatus: vi.fn(),
      completeSetup: vi.fn(),
      signIn: vi.fn(),
      currentSession: vi.fn(),
      signOut: vi.fn(),
      searchLogs: vi.fn(),
      summarizeLogs: vi.fn(),
      home: vi.fn(),
      listKeys: vi.fn(),
      createKey: vi.fn(),
      revokeKey: vi.fn(),
    },
  };
});

const admin = { id: "1", email: "root@example.com", displayName: "Root", role: "Admin" as const };

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
  vi.mocked(api.searchLogs).mockResolvedValue({ items: [], nextCursor: null });
  vi.mocked(api.summarizeLogs).mockResolvedValue({ bucketSeconds: 120, buckets: [], services: [] });
  vi.mocked(api.home).mockResolvedValue(clearSnapshot);
});

describe("App", () => {
  it("shows first-run setup on a fresh installation", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(null);
    vi.mocked(api.setupStatus).mockResolvedValue({ required: true });

    renderAt("/");

    expect(await screen.findByRole("heading", { name: "Create the first administrator" })).toBeInTheDocument();
  });

  it("shows sign-in when nobody is signed in, and the Log Explorer after signing in", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(null);
    vi.mocked(api.setupStatus).mockResolvedValue({ required: false });
    vi.mocked(api.signIn).mockResolvedValue({ session: admin });
    const user = userEvent.setup();

    renderAt("/logs");
    await user.type(await screen.findByLabelText("Email"), "root@example.com");
    await user.type(screen.getByLabelText("Password"), "correct horse battery");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(api.signIn).toHaveBeenCalledWith("root@example.com", "correct horse battery");
    expect(await screen.findByRole("heading", { name: "Logs" })).toBeInTheDocument();
  });

  it("explains a failed sign-in without leaving the form", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(null);
    vi.mocked(api.setupStatus).mockResolvedValue({ required: false });
    vi.mocked(api.signIn).mockRejectedValue(new ApiError(401, "The email or password is wrong."));
    const user = userEvent.setup();

    renderAt("/");
    await user.type(await screen.findByLabelText("Email"), "root@example.com");
    await user.type(screen.getByLabelText("Password"), "wrong password!");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The email or password is wrong.");
    expect(screen.getByLabelText("Email")).toBeInTheDocument();
  });

  it("completes setup with the token from the link, then asks to sign in", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(null);
    vi.mocked(api.setupStatus).mockResolvedValue({ required: true });
    vi.mocked(api.completeSetup).mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderAt("/setup?token=abc123");
    await user.type(await screen.findByLabelText("Email"), "root@example.com");
    await user.type(screen.getByLabelText("Name"), "Root");
    await user.type(screen.getByLabelText("Password"), "correct horse battery");
    await user.click(screen.getByRole("button", { name: "Create administrator" }));

    expect(api.completeSetup).toHaveBeenCalledWith({
      token: "abc123",
      email: "root@example.com",
      displayName: "Root",
      password: "correct horse battery",
    });
    expect(await screen.findByRole("status")).toHaveTextContent("Administrator created. Sign in to continue.");
    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });

  it("offers ingestion keys to administrators only", async () => {
    vi.mocked(api.currentSession).mockResolvedValue({ ...admin, role: "Viewer" });
    vi.mocked(api.listKeys).mockResolvedValue([]);

    renderAt("/keys");

    expect(await screen.findByRole("heading", { name: "Clear skies" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Keys" })).not.toBeInTheDocument();
  });

  it("opens ingestion keys for an administrator", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(admin);
    vi.mocked(api.listKeys).mockResolvedValue([]);

    renderAt("/keys");

    expect(await screen.findByRole("heading", { name: "Ingestion keys" })).toBeInTheDocument();
  });

  it("shows the home straight away when a session exists", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(admin);

    renderAt("/");

    expect(await screen.findByRole("heading", { name: "Clear skies" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Home" })).toHaveAttribute("aria-current", "page");
  });

  it("sends unknown paths to the home", async () => {
    vi.mocked(api.currentSession).mockResolvedValue(admin);

    renderAt("/no-such-screen");

    expect(await screen.findByRole("heading", { name: "Clear skies" })).toBeInTheDocument();
  });
});
