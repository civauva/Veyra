import { describe, expect, it } from "vitest";

import { navFor, screenOf } from "./look";
import { looks } from "./looks";
import { routes } from "./routes";

const lookList = Object.values(looks);

describe("routes", () => {
  it("routes_EveryIdAndPath_IsUnique", () => {
    expect(new Set(routes.map((route) => route.id)).size).toBe(routes.length);
    expect(new Set(routes.map((route) => route.path)).size).toBe(routes.length);
  });

  it("routes_OnlyTheHome_LeavesItsScreenToTheLook", () => {
    expect(routes.filter((route) => !route.screen).map((route) => route.id)).toEqual(["home"]);
  });
});

describe.each(lookList.map((look) => [look.id, look] as const))("the %s look", (_id, look) => {
  it("nav_EveryMenuRoute_AppearsExactlyOnce", () => {
    const placed = look.nav.flatMap((group) => group.entries.map((entry) => entry.route));
    const menu = routes.filter((route) => route.place === "menu").map((route) => route.id);

    expect([...placed].sort()).toEqual([...menu].sort());
  });

  it("screenOf_EveryRoute_ResolvesToAScreen", () => {
    for (const route of routes) {
      expect(screenOf(look, route), route.id).toBeTypeOf("function");
    }
  });

  it("navFor_Viewer_LeavesOutAdminRoutesAndEmptyGroups", () => {
    const groups = navFor(look, "Viewer");
    const shown = groups.flatMap((group) => group.items.map((item) => item.route.id));

    expect(groups.every((group) => group.items.length > 0)).toBe(true);
    expect(shown.some((id) => routes.find((route) => route.id === id)?.admin)).toBe(false);
  });
});

describe("the Forecast look's menu", () => {
  it("navFor_Admin_ListsTheScreensThenAdmin", () => {
    const groups = navFor(looks.forecast, "Admin").map((group) => ({
      title: group.title,
      items: group.items.map((item) => `${item.label} ${item.route.path}`),
    }));

    expect(groups).toEqual([
      {
        title: null,
        items: [
          "Home /",
          "Logs /logs",
          "Traces /traces",
          "Metrics /metrics",
          "Services /resources/services",
          "Hosts /resources/hosts",
          "Databases /resources/databases",
          "Jobs /jobs",
          "Incidents /incidents",
          "Alerts /alerts",
        ],
      },
      {
        title: "Admin",
        items: [
          "Integrations /integrations",
          "Ingestion keys /keys",
          "Users /users",
          "Notifications /notifications",
          "Settings /settings",
        ],
      },
    ]);
  });

  it("navFor_Editor_HasNoAdminGroup", () => {
    expect(navFor(looks.forecast, "Editor").map((group) => group.title)).toEqual([null]);
  });
});
