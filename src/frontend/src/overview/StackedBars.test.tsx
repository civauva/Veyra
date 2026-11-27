import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { StackedBars } from "./StackedBars";

describe("StackedBars", () => {
  it("StackedBars_Keyboard_ReadsEachBarWithItsLayers", () => {
    render(
      <StackedBars
        title="Telemetry ingestion"
        labels={["10:00", "10:01", "now"]}
        steps={["10:00", "10:01", "10:02"]}
        layers={[
          { name: "Logs", color: "teal", values: [5, 1200, 7] },
          { name: "Spans", color: "violet", values: [2, 3, 4] },
        ]}
      />,
    );

    const plot = screen.getByLabelText("Telemetry ingestion: use the arrow keys to read each bar");
    fireEvent.focus(plot);
    expect(within(screen.getByRole("status")).getByText("10:02")).toBeInTheDocument();

    fireEvent.keyDown(plot, { key: "ArrowLeft" });
    const tip = screen.getByRole("status");
    expect(within(tip).getByText("10:01")).toBeInTheDocument();
    expect(within(tip).getByText("1,200")).toBeInTheDocument();
  });
});
