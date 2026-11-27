import type { NavGroup } from "../../app/look";
import type { RouteId } from "../../app/routes";

/**
 * The Console look's menu (VEY-69 mockups): what to watch, what runs, what to act on, and what to manage.
 */
export const consoleNav: NavGroup[] = [
  {
    title: "Observe",
    entries: [{ route: "home", label: "Overview" }, { route: "logs" }, { route: "traces" }, { route: "metrics" }],
  },
  {
    title: "Resources",
    entries: [{ route: "services" }, { route: "hosts" }, { route: "databases" }, { route: "jobs" }],
  },
  { title: "Respond", entries: [{ route: "incidents" }, { route: "alerts" }] },
  {
    title: "Manage",
    entries: [
      { route: "integrations" },
      { route: "keys" },
      { route: "users" },
      { route: "notifications" },
      { route: "settings" },
    ],
  },
];

/**
 * The menu entry a detail screen belongs to in Console, by the start of its address. Console files a server's, a
 * host's, and a service's screens under Resources, though their addresses start with `/metrics`, so the menu marks
 * the entry the person came from.
 */
export const consoleOwners: { prefix: string; route: RouteId }[] = [
  { prefix: "/metrics/databases/", route: "databases" },
  { prefix: "/metrics/hosts/", route: "hosts" },
  { prefix: "/metrics/services/", route: "services" },
];
