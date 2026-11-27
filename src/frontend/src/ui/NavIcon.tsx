/** The pictures of the sidebar: one per screen, and the few its buttons need. */
export type NavIconName =
  | "home"
  | "logs"
  | "traces"
  | "metrics"
  | "services"
  | "hosts"
  | "databases"
  | "jobs"
  | "incidents"
  | "alerts"
  | "integrations"
  | "keys"
  | "users"
  | "notifications"
  | "settings"
  | "collapse"
  | "expand"
  | "menu"
  | "close"
  | "more"
  | "choose"
  | "check";

/** Props for {@link NavIcon}. */
interface NavIconProps {
  /** Which picture. */
  name: NavIconName;
  /** The width and height in pixels. Defaults to 20. */
  size?: number;
  /** An extra class, for colour and the current-screen tint. */
  className?: string;
}

/** The outlines, drawn on a 24-unit grid with a 1.7-unit stroke, as in the approved VEY-26 mockups. */
const paths: Record<NavIconName, string> = {
  home: "M4 11l8-6.5 8 6.5V20h-5.5v-5h-5v5H4z",
  logs: "M5 6h14M5 10h14M5 14h10M5 18h7",
  traces: "M4 6h10M7 10h9M9 14h11M12 18h6",
  metrics: "M4 19h16M6 16l4-5 3.5 3L19 7",
  services: "M4.5 4.5h6v6h-6zM13.5 4.5h6v6h-6zM4.5 13.5h6v6h-6zM13.5 13.5h6v6h-6z",
  hosts: "M4 5h16v6H4zM4 13h16v6H4zM7.5 8h.01M7.5 16h.01",
  databases:
    "M5 6c0-1.4 3.1-2.5 7-2.5s7 1.1 7 2.5-3.1 2.5-7 2.5S5 7.4 5 6zM5 6v12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5V6M5 12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5",
  jobs: "M4.5 6.5h9M4.5 12h6M4.5 17.5h9M17 4.5l2.5 2-2.5 2M14.5 12a3.5 3.5 0 1 0 7 0 3.5 3.5 0 0 0-7 0zM18 10.6V12l1 .8",
  incidents: "M12 4 21.5 20h-19zM12 10v4.5M12 17.5h.01",
  alerts: "M6.5 16.5V11a5.5 5.5 0 0 1 11 0v5.5l1.5 1.5H5zM10 20.5h4",
  integrations: "M9 3.5v4.5M15 3.5v4.5M6.5 8h11v3.5a5.5 5.5 0 0 1-11 0zM12 17v3.5",
  keys: "M11.6 12H20M17 12v3M20 12v2.5M8 15.6a3.6 3.6 0 1 0 0-7.2 3.6 3.6 0 0 0 0 7.2z",
  users:
    "M9 12.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4zM3.5 19a5.5 5.5 0 0 1 11 0M15.5 6.2a3 3 0 0 1 0 5.6M17 14.2a5 5 0 0 1 3.5 4.8",
  notifications: "M4 12l16-7-6 15-2.5-6.5z",
  settings:
    "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM10.3 3.5h3.4l.5 2.4 1.8 1 2.3-.8 1.7 2.9-1.8 1.6v2l1.8 1.6-1.7 2.9-2.3-.8-1.8 1-.5 2.4h-3.4l-.5-2.4-1.8-1-2.3.8-1.7-2.9 1.8-1.6v-2L4.1 9l1.7-2.9 2.3.8 1.8-1z",
  collapse: "M14 7l-5 5 5 5M19 7l-5 5 5 5",
  expand: "M10 7l5 5-5 5M5 7l5 5-5 5",
  menu: "M4 7h16M4 12h16M4 17h16",
  close: "M6 6l12 12M18 6L6 18",
  more: "M6 12h.01M12 12h.01M18 12h.01",
  choose: "M8 9.5l4-4 4 4M8 14.5l4 4 4-4",
  check: "M6 12.5l4 4 8-9",
};

/**
 * A sidebar picture. Decorative: the link or button it sits in always carries the words.
 */
export function NavIcon({ name, size = 20, className }: NavIconProps) {
  return (
    <svg
      className={className ? `nav-icon ${className}` : "nav-icon"}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={name === "more" ? 3 : 1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name]} />
    </svg>
  );
}
