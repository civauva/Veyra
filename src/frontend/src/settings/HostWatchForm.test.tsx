import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type * as client from "../api/client";
import { api } from "../api/client";
import type { Agent } from "../api/types";
import { builtInAgent } from "./testMonitoring";
import { HostWatchForm } from "./HostWatchForm";

vi.mock("../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof client>();

  return { ...original, api: { setAgentHost: vi.fn() } };
});

const windowsAgent: Agent = {
  ...builtInAgent,
  id: "a2",
  name: "Web server",
  placement: "Remote",
  operatingSystem: "Windows Server 2022 Datacenter",
};

const linuxAgent: Agent = { ...windowsAgent, operatingSystem: "Ubuntu 24.04.1 LTS" };

beforeEach(() => {
  vi.mocked(api.setAgentHost).mockReset();
  vi.mocked(api.setAgentHost).mockResolvedValue(windowsAgent);
});

describe("HostWatchForm", () => {
  it("shows the Event Log and IIS switches for a Windows agent and saves the values", async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    render(<HostWatchForm agent={windowsAgent} onSaved={onSaved} />);
    expect(screen.queryByRole("switch", { name: "Event Log errors" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("switch", { name: "Watch this host" }));
    await user.click(screen.getByRole("switch", { name: "Event Log errors" }));
    await user.click(screen.getByRole("switch", { name: "IIS sites" }));
    await user.clear(screen.getByRole("textbox", { name: "Environment" }));
    await user.type(screen.getByRole("textbox", { name: "Environment" }), "staging");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(api.setAgentHost).toHaveBeenCalledWith("a2", {
      host: true,
      eventLog: true,
      iis: true,
      docker: false,
      environment: "staging",
    });
    expect(onSaved).toHaveBeenCalled();
    expect(screen.queryByRole("switch", { name: "Docker containers" })).not.toBeInTheDocument();
  });

  it("offers no Event Log or IIS switches for a Linux agent", async () => {
    const user = userEvent.setup();
    render(<HostWatchForm agent={linuxAgent} onSaved={vi.fn()} />);

    await user.click(screen.getByRole("switch", { name: "Watch this host" }));

    expect(screen.getByRole("textbox", { name: "Environment" })).toHaveValue("production");
    expect(screen.queryByRole("switch", { name: "Event Log errors" })).not.toBeInTheDocument();
    expect(screen.queryByRole("switch", { name: "IIS sites" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(api.setAgentHost).toHaveBeenCalledWith("a2", {
      host: true,
      eventLog: false,
      iis: false,
      docker: false,
      environment: "production",
    });
  });

  it("shows the Docker switch for a Linux agent and saves it", async () => {
    const user = userEvent.setup();
    render(<HostWatchForm agent={linuxAgent} onSaved={vi.fn()} />);
    expect(screen.queryByRole("switch", { name: "Docker containers" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("switch", { name: "Watch this host" }));
    await user.click(screen.getByRole("switch", { name: "Docker containers" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(api.setAgentHost).toHaveBeenCalledWith("a2", {
      host: true,
      eventLog: false,
      iis: false,
      docker: true,
      environment: "production",
    });
  });

  it("turns Docker off with the host", async () => {
    const user = userEvent.setup();
    render(<HostWatchForm agent={{ ...linuxAgent, host: true, docker: true }} onSaved={vi.fn()} />);
    expect(screen.getByRole("switch", { name: "Docker containers" })).toBeChecked();

    await user.click(screen.getByRole("switch", { name: "Watch this host" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(api.setAgentHost).toHaveBeenCalledWith("a2", expect.objectContaining({ host: false, docker: false }));
  });

  it("offers Save when only Docker changed", async () => {
    const user = userEvent.setup();
    render(<HostWatchForm agent={{ ...linuxAgent, host: true }} onSaved={vi.fn()} />);
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("switch", { name: "Docker containers" }));

    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });

  it("shows Veyra's reason when the setting is refused", async () => {
    const user = userEvent.setup();
    vi.mocked(api.setAgentHost).mockRejectedValue(new Error("The Event Log and IIS are read on Windows only."));
    render(<HostWatchForm agent={windowsAgent} onSaved={vi.fn()} />);

    await user.click(screen.getByRole("switch", { name: "Watch this host" }));
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("read on Windows only");
  });

  it("offers Save only after something changed", async () => {
    const user = userEvent.setup();
    render(<HostWatchForm agent={{ ...windowsAgent, host: true }} onSaved={vi.fn()} />);
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("switch", { name: "Watch this host" }));

    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });
});
