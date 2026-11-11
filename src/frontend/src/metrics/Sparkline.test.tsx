import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Sparkline } from "./Sparkline";

const line = (values: (number | null)[]) => ({
  label: "p95",
  latest: values.at(-1) ?? null,
  points: values.map((value, index) => ({ at: new Date(Date.UTC(2026, 10, 10, 12, index)).toISOString(), value })),
});

describe("Sparkline", () => {
  it("fills only under the points that arrived", () => {
    // Five points over the 380-wide box: x = 0, 95, 190, 285, 380. The first two are missing.
    const { container } = render(<Sparkline line={line([null, null, 1, 2, 1])} color="red" />);

    const fill = container.querySelectorAll("path")[0]?.getAttribute("d");

    expect(fill).toMatch(/^M190\.0 /);
    expect(fill).toMatch(/L380\.0 40 L190\.0 40 Z$/);
  });
});
