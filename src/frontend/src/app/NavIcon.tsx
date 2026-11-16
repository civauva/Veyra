/** The pictures of the sidebar: one per screen, and the few its buttons need. */
export type NavIconName =
  | "home"
  | "logs"
  | "traces"
  | "metrics"
  | "alerts"
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
  alerts: "M6.5 16.5V11a5.5 5.5 0 0 1 11 0v5.5l1.5 1.5H5zM10 20.5h4",
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
