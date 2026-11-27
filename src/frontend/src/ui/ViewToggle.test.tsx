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
  it("ViewSwitch_Untouched_NamesTheSimpleView", () => {
    render(<ViewSwitch />);

    expect(screen.getByRole("button", { name: "Simple view" })).toHaveAttribute("aria-expanded", "false");
  });

  it("ViewSwitch_AdvancedChosen_SwitchesEveryScreenAndBack", async () => {
    const user = userEvent.setup();
    render(
      <>
        <ViewSwitch />
        <Reader />
      </>,
    );

    await user.click(screen.getByRole("button", { name: "Simple view" }));
    expect(screen.getByRole("menuitemradio", { name: /^Simple/ })).toHaveFocus();
    await user.keyboard("{ArrowDown}{Enter}");
    expect(screen.getByText("Showing advanced")).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Advanced view" }));
    await user.click(screen.getByRole("menuitemradio", { name: /^Simple/ }));
    expect(screen.getByText("Showing simple")).toBeVisible();
  });

  it("ViewSwitch_Escape_ClosesTheMenuAndKeepsTheView", async () => {
    const user = userEvent.setup();
    render(<ViewSwitch />);

    await user.click(screen.getByRole("button", { name: "Simple view" }));
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Simple view" })).toHaveFocus();
  });
});
