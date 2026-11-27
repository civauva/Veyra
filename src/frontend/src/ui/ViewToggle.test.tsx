import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";

import { useViewMode, ViewSwitch, ViewToggle } from "./ViewToggle";

/** Shows the mode a component sees, as a screen would. */
function Reader() {
  const [mode] = useViewMode();

  return <p>Showing {mode}</p>;
}

beforeEach(() => localStorage.clear());

describe("ViewToggle", () => {
  it("starts Simple", () => {
    render(<ViewToggle />);

    expect(screen.getByRole("button", { name: "Simple" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Advanced" })).toHaveAttribute("aria-pressed", "false");
  });

  it("switches every screen at once and remembers the choice", async () => {
    const user = userEvent.setup();
    const { unmount } = render(
      <>
        <ViewToggle />
        <Reader />
      </>,
    );

    await user.click(screen.getByRole("button", { name: "Advanced" }));

    expect(screen.getByText("Showing advanced")).toBeVisible();
    unmount();
    render(<Reader />);
    expect(screen.getByText("Showing advanced")).toBeVisible();
  });

  it("is a named group a keyboard can reach", () => {
    render(<ViewToggle />);

    expect(screen.getByRole("group", { name: "View" })).toBeVisible();
  });
});

describe("ViewSwitch", () => {
  it("ViewSwitch_Untouched_IsOffForSimple", () => {
    render(<ViewSwitch />);

    expect(screen.getByRole("switch", { name: "Advanced" })).toHaveAttribute("aria-checked", "false");
  });

  it("ViewSwitch_Clicked_SwitchesEveryScreenToAdvancedAndBack", async () => {
    const user = userEvent.setup();
    render(
      <>
        <ViewSwitch />
        <Reader />
      </>,
    );

    await user.click(screen.getByRole("switch", { name: "Advanced" }));
    expect(screen.getByRole("switch", { name: "Advanced" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText("Showing advanced")).toBeVisible();

    await user.click(screen.getByRole("switch", { name: "Advanced" }));
    expect(screen.getByText("Showing simple")).toBeVisible();
  });
});
