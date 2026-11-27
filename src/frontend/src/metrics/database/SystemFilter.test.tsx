import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";

import { sqlProdBackupAlert } from "./testDatabase";
import { ServerAlerts, SystemHidden, SystemToggle, useSystemShown } from "./SystemFilter";

/** Shows whether system things are shown, as a tab reads it. */
function Reader() {
  const [shown] = useSystemShown();

  return <p>{shown ? "system shown" : "system hidden"}</p>;
}

beforeEach(() => {
  localStorage.clear();
});

describe("SystemToggle", () => {
  it("is off until switched on, and the choice holds for every tab and visit", async () => {
    const user = userEvent.setup();
    const { unmount } = render(
      <>
        <SystemToggle />
        <Reader />
      </>,
    );

    const toggle = screen.getByRole("button", { name: "System" });
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByText("system hidden")).toBeInTheDocument();

    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("system shown")).toBeInTheDocument();

    unmount();
    render(<Reader />);
    expect(screen.getByText("system shown")).toBeInTheDocument();
  });
});

describe("SystemHidden", () => {
  it("says how many system rows are hidden, and shows them on request", async () => {
    const user = userEvent.setup();
    render(
      <>
        <SystemHidden hidden={{ count: 3, names: [] }} noun="statement" />
        <Reader />
      </>,
    );

    expect(screen.getByRole("note")).toHaveTextContent("3 system statements hidden");

    await user.click(screen.getByRole("button", { name: "Show them" }));

    expect(screen.queryByRole("note")).not.toBeInTheDocument();
    expect(screen.getByText("system shown")).toBeInTheDocument();
  });

  it("names a hidden database that has an open alert, so the switch never hides a problem", () => {
    const alert = { ...sqlProdBackupAlert, block: { ...sqlProdBackupAlert.block, part: "msdb" } };
    render(
      <ServerAlerts.Provider value={[alert]}>
        <SystemHidden hidden={{ count: 2, names: ["model", "msdb"] }} noun="database" />
      </ServerAlerts.Provider>,
    );

    expect(screen.getByRole("note")).toHaveTextContent("2 system databases hidden. msdb has an open alert.");
  });

  it("says nothing when no system row is hidden", () => {
    const { container } = render(<SystemHidden hidden={{ count: 0, names: [] }} noun="job" />);

    expect(container).toBeEmptyDOMElement();
  });
});
