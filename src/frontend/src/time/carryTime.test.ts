import { describe, expect, it } from "vitest";

import { carryTime } from "./carryTime";

describe("carryTime", () => {
  it("carryTime_Link_KeepsTheRangeAndDropsOtherFilters", () => {
    const current = new URLSearchParams("range=7d&severity=Error&service=api");

    expect(carryTime("/metrics", current)).toBe("/metrics?range=7d");
    expect(carryTime("/", new URLSearchParams("from=2026-11-25T09:00:00.000Z&to=2026-11-26T09:00:00.000Z"))).toBe(
      "/?from=2026-11-25T09%3A00%3A00.000Z&to=2026-11-26T09%3A00%3A00.000Z",
    );
    expect(carryTime("/logs", new URLSearchParams(""))).toBe("/logs");
  });

  it("carryTime_PathWithItsOwnFilter_KeepsTheFilterAndAddsTheRange", () => {
    expect(carryTime("/traces?show=failed", new URLSearchParams("range=7d&service=api"))).toBe(
      "/traces?show=failed&range=7d",
    );
    expect(carryTime("/settings?section=Agents", new URLSearchParams(""))).toBe("/settings?section=Agents");
  });

  it("carryTime_PathWithAnAnchor_KeepsTheAnchorLast", () => {
    expect(carryTime("/#databases", new URLSearchParams("range=7d"))).toBe("/?range=7d#databases");
    expect(carryTime("/alerts/a1#fix", new URLSearchParams(""))).toBe("/alerts/a1#fix");
  });
});
