import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { HelpTip } from "./HelpTip";

function renderTip() {
  return render(
    <p>
      Peak <HelpTip label="Peak">The worst value while the alert was open.</HelpTip>{" "}
      <button type="button">Other</button>
    </p>,
  );
}

describe("HelpTip", () => {
  it("is named after what it explains and describes itself while open", async () => {
    const user = userEvent.setup();
    renderTip();

    const button = screen.getByRole("button", { name: "What Peak means" });
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

    await user.click(button);

    expect(screen.getByRole("tooltip")).toHaveTextContent("The worst value while the alert was open.");
    expect(button).toHaveAttribute("aria-expanded", "true");
    expect(button).toHaveAccessibleDescription("The worst value while the alert was open.");
  });

  it("opens on keyboard focus and closes with Escape", async () => {
    const user = userEvent.setup();
    renderTip();

    await user.tab();
    expect(screen.getByRole("tooltip")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("stays open after a click while the pointer moves away, until a click elsewhere", async () => {
    const user = userEvent.setup();
    renderTip();

    await user.click(screen.getByRole("button", { name: "What Peak means" }));
    await user.unhover(screen.getByRole("button", { name: "What Peak means" }));
    expect(screen.getByRole("tooltip")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Other" }));
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("stays open while the page scrolls with its question mark in view", async () => {
    const user = userEvent.setup();
    renderTip();

    await user.click(screen.getByRole("button", { name: "What Peak means" }));
    fireEvent.scroll(window);

    expect(screen.getByRole("tooltip")).toBeInTheDocument();
  });
});
