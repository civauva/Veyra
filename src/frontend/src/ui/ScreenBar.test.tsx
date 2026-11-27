import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it } from "vitest";

import { ScreenBar } from "./ScreenBar";

beforeEach(() => {
  localStorage.clear();
});

describe("ScreenBar", () => {
  it("says where the screen is, with links back up the trail", () => {
    render(
      <MemoryRouter>
        <ScreenBar trail={[{ label: "Metrics", to: "/metrics" }, { label: "web-01" }]} />
      </MemoryRouter>,
    );

    const trail = screen.getByRole("navigation", { name: "Where you are" });
    expect(within(trail).getByRole("link", { name: "Metrics" })).toHaveAttribute("href", "/metrics");
    expect(within(trail).getByText("web-01")).toHaveAttribute("aria-current", "page");
  });

  it("puts the screen's own controls before the view toggle, which is always last", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <MemoryRouter>
        <ScreenBar trail={[{ label: "Metrics" }]} view>
          <button type="button">6h</button>
        </ScreenBar>
      </MemoryRouter>,
    );

    const tools = container.querySelector(".screen-tools");
    expect(tools?.lastElementChild).toHaveAttribute("aria-label", "View");
    expect(tools?.firstElementChild).toHaveTextContent("6h");

    await user.click(screen.getByRole("button", { name: "Advanced" }));
    expect(screen.getByRole("button", { name: "Advanced" })).toHaveAttribute("aria-pressed", "true");
  });

  it("has no view toggle on a screen without views", () => {
    render(
      <MemoryRouter>
        <ScreenBar trail={[{ label: "Ingestion keys" }]} />
      </MemoryRouter>,
    );

    expect(screen.queryByRole("group", { name: "View" })).not.toBeInTheDocument();
  });
});
