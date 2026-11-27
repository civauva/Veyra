import type { NavGroup } from "../../app/look";

/** The Forecast look's menu (VEY-26): the screens, then Admin at the foot of the sidebar. */
export const forecastNav: NavGroup[] = [
  {
    title: null,
    entries: [
      { route: "home" },
      { route: "logs" },
      { route: "traces" },
      { route: "metrics" },
      { route: "services" },
      { route: "hosts", label: "Hosts" },
      { route: "databases" },
      { route: "jobs", label: "Jobs" },
      { route: "incidents" },
      { route: "alerts" },
    ],
  },
  {
    title: "Admin",
    entries: [
      { route: "integrations" },
      { route: "keys" },
      { route: "users" },
      { route: "notifications" },
      { route: "settings" },
    ],
  },
];
