import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ChartLine } from "../api/types";
import { LineChart } from "./LineChart";

/** A line of the given values, a minute apart. */
function lineOf(label: string, values: (number | null)[]): ChartLine {
  const known = values.filter((value): value is number => value !== null);

  return {
    label,
    latest: known.at(-1) ?? null,
    points: values.map((value, index) => ({ at: new Date(Date.UTC(2026, 10, 26, 10, index)).toISOString(), value })),
  };
}

describe("LineChart", () => {
  it("LineChart_NoValues_ShowsOneZeroAndSaysThereIsNoData", () => {
    const { container } = render(
      <LineChart
        title="Requests"
        format={String}
        series={[{ line: lineOf("Requests", [null, null, null]), color: "red" }]}
      />,
    );

    const axis = [...container.querySelectorAll("text.chart-axis")].map((text) => text.textContent);
    expect(axis.filter((text) => text === "0")).toHaveLength(1);
    expect(screen.getByText("No data in this range")).toBeInTheDocument();
  });

  it("LineChart_AllZero_ShowsOneZeroAndNoRepeatedLabels", () => {
    const { container } = render(
      <LineChart title="Requests" format={String} series={[{ line: lineOf("Requests", [0, 0, 0]), color: "red" }]} />,
    );

    const values = [...container.querySelectorAll("text.chart-axis.value")].map((text) => text.textContent);
    expect(new Set(values).size).toBe(values.length);
  });

  it("LineChart_TimeLabels_EndInsideThePlot", () => {
    const { container } = render(
      <LineChart
        title="Requests"
        format={String}
        series={[{ line: lineOf("Requests", [1, 2, 3, 4]), color: "red" }]}
      />,
    );

    const times = [...container.querySelectorAll("text.chart-axis.time")];
    expect(times.at(0)?.getAttribute("text-anchor")).toBe("start");
    expect(times.at(-1)?.getAttribute("text-anchor")).toBe("end");
  });

  it("LineChart_Height_IsTheLeastItDraws", () => {
    const { container } = render(
      <LineChart
        title="Requests"
        format={String}
        height={210}
        series={[{ line: lineOf("Requests", [1, 2]), color: "red" }]}
      />,
    );

    expect(container.querySelector(".line-chart-plot")).toHaveStyle({ minHeight: "210px" });
    expect(container.querySelector("svg")?.getAttribute("height")).toBe("210");
  });
});
