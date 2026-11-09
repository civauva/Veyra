import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { IngestionKey } from "../api/types";
import { KeysScreen } from "./KeysScreen";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { listKeys: vi.fn(), createKey: vi.fn(), revokeKey: vi.fn() } };
});

const collector: IngestionKey = {
  id: "k1",
  name: "web-01 collector",
  displayPrefix: "vyk_AbCdEfGh",
  environments: ["production"],
  createdAt: "2026-11-01T09:00:00Z",
  expiresAt: null,
  revokedAt: null,
  lastUsedAt: "2026-11-09T07:30:00Z",
};

const old: IngestionKey = {
  id: "k2",
  name: "old laptop",
  displayPrefix: "vyk_ZyXwVuTs",
  environments: null,
  createdAt: "2026-10-01T09:00:00Z",
  expiresAt: null,
  revokedAt: "2026-10-20T09:00:00Z",
  lastUsedAt: null,
};

beforeEach(() => {
  vi.mocked(api.listKeys).mockResolvedValue([collector, old]);
});

describe("KeysScreen", () => {
  it("lists the keys with their environments, use, and state", async () => {
    render(<KeysScreen />);

    const active = await screen.findByRole("row", { name: /web-01 collector/ });
    expect(within(active).getByText("vyk_AbCdEfGh…")).toBeInTheDocument();
    expect(within(active).getByText("production")).toBeInTheDocument();
    expect(within(active).getByText("Active")).toBeInTheDocument();

    const revoked = screen.getByRole("row", { name: /old laptop/ });
    expect(within(revoked).getByText("All")).toBeInTheDocument();
    expect(within(revoked).getAllByText("Never")).toHaveLength(2);
    expect(within(revoked).getByText("Revoked")).toBeInTheDocument();
    expect(within(revoked).queryByRole("button", { name: /Revoke/ })).not.toBeInTheDocument();
  });

  it("creates a key and shows its secret once", async () => {
    vi.mocked(api.createKey).mockResolvedValue({
      id: "k3",
      name: "db-01 collector",
      displayPrefix: "vyk_NewKey12",
      secret: "vyk_NewKey12_secret-part",
    });
    const user = userEvent.setup();
    render(<KeysScreen />);

    await user.type(await screen.findByLabelText("Name"), "db-01 collector");
    await user.type(screen.getByLabelText(/Environments/), "production, staging");
    await user.click(screen.getByRole("button", { name: "Create key" }));

    expect(api.createKey).toHaveBeenCalledWith({
      name: "db-01 collector",
      environments: ["production", "staging"],
      expiresAt: null,
    });
    const created = await screen.findByRole("region", { name: "New key" });
    expect(within(created).getByText("vyk_NewKey12_secret-part")).toBeInTheDocument();
    expect(api.listKeys).toHaveBeenCalledTimes(2);

    await user.click(within(created).getByRole("button", { name: "I have copied it" }));

    expect(screen.queryByText("vyk_NewKey12_secret-part")).not.toBeInTheDocument();
  });

  it("sends an expiry date as the end of that day in UTC", async () => {
    vi.mocked(api.createKey).mockResolvedValue({ id: "k3", name: "temp", displayPrefix: "vyk_x", secret: "s" });
    const user = userEvent.setup();
    render(<KeysScreen />);

    await user.type(await screen.findByLabelText("Name"), "temp");
    await user.type(screen.getByLabelText(/Expires/), "2026-12-31");
    await user.click(screen.getByRole("button", { name: "Create key" }));

    expect(api.createKey).toHaveBeenCalledWith({
      name: "temp",
      environments: null,
      expiresAt: "2026-12-31T23:59:59.000Z",
    });
  });

  it("revokes a key only after the person confirms", async () => {
    vi.mocked(api.revokeKey).mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<KeysScreen />);

    await user.click(await screen.findByRole("button", { name: "Revoke web-01 collector" }));
    expect(api.revokeKey).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Yes, revoke" }));

    expect(api.revokeKey).toHaveBeenCalledWith("k1");
    await waitFor(() => expect(api.listKeys).toHaveBeenCalledTimes(2));
  });

  it("explains when the keys cannot be loaded", async () => {
    vi.mocked(api.listKeys).mockRejectedValue(new Error("Veyra answered with status 500."));
    render(<KeysScreen />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Veyra answered with status 500.");
  });
});
