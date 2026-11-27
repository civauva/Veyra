/** The pictures the Console look draws, as in the VEY-69 mockups: thin strokes on a 24 grid. */
const paths = {
  overview: "M3 13h8V3H3zM13 21h8V11h-8zM3 21h8v-6H3zM13 3v6h8V3z",
  logs: "M4 6h16M4 10h16M4 14h10M4 18h7",
  traces: "M4 5h7M8 10h9M12 15h8M6 20h5M4 5v15",
  metrics: "M4 20V10M10 20V4M16 20v-7M22 20H2",
  incidents: "M12 4 21.5 20h-19zM12 10v4.5M12 17.5h.01",
  alerts: "M6 16V11a6 6 0 1 1 12 0v5l2 2H4zM10 21h4",
  integrations: "M9 2.5v5M15 2.5v5M5.5 7.5h13v4a6.5 6.5 0 0 1-13 0zM12 18v3.5",
  eye: "M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12zM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0z",
  keys: "M11 12l9-9M17 6l3 3M15 8l2 2M12 15a4 4 0 1 1-8 0 4 4 0 0 1 8 0z",
  users:
    "M12.5 8a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0zM2.5 20c.8-3.5 3.4-5.5 6.5-5.5s5.7 2 6.5 5.5M16 4.5a3.5 3.5 0 0 1 0 7" +
    "M18 14.8c1.8.8 3 2.6 3.5 5.2",
  notifications: "M4 6h16v10H8l-4 4z",
  settings:
    "M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17" +
    "M17 7l2.1-2.1",
  layers: "M12 3l9 5-9 5-9-5zM3 13l9 5 9-5",
  search: "M18 11a7 7 0 1 1-14 0 7 7 0 0 1 14 0zM20 20l-3.5-3.5",
  chevron: "M6 9l6 6 6-6",
  menu: "M4 7h16M4 12h16M4 17h16",
  close: "M6 6l12 12M18 6L6 18",
  check: "M5 12.5l4.5 4.5L19 7.5",
  calendar: "M3 5h18v16H3zM3 10h18M8 3v4M16 3v4",
  refresh: "M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7",
  play: "M7 4l13 8-13 8z",
  help: "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0zM9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6V14M12 17.5h.01",
  hosts: "M3 4h18v7H3zM3 13h18v7H3zM7 7.5h.01M7 16.5h.01",
  container: "M3 7l9-4 9 4v10l-9 4-9-4zM3 7l9 4 9-4M12 11v10",
  database:
    "M4 5.5c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 5.5v13c0 1.7 3.6 3 8 3s8-1.3 8-3v-13" +
    "M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3",
  services: "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
  jobs: "M3 6h8M3 12h5M3 18h8M13.5 12a4.5 4.5 0 1 0 9 0 4.5 4.5 0 0 0-9 0zM18 9.5V12l1.6 1.2M14 4.5l2 1.5-2 1.5",
  book: "M4 4h6a3 3 0 0 1 3 3v13a2 2 0 0 0-2-2H4zM20 4h-6a3 3 0 0 0-3 3v13a2 2 0 0 1 2-2h7z",
  keyboard: "M3 6h18v12H3zM7 10h.01M11 10h.01M15 10h.01M7 14h10",
} as const;

/** A picture's name. */
export type ConsoleIconName = keyof typeof paths;

/** Props for {@link ConsoleIcon}. */
interface ConsoleIconProps {
  /** Which picture. */
  name: ConsoleIconName;
  /** Its width and height in pixels. */
  size?: number;
  /** A class for placing it. */
  className?: string;
}

/** One Console picture. Decorative: the words beside it, or the control's label, say what it is. */
export function ConsoleIcon({ name, size = 16, className }: ConsoleIconProps) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name]} />
    </svg>
  );
}

/** The Veyra mark of the Console look: a V in the teal of the look. */
export function ConsoleMark() {
  return (
    <svg width="30" height="26" viewBox="0 0 30 26" aria-hidden="true">
      <defs>
        <linearGradient id="cn-mark-fill" x1="0" x2="1" y1="0" y2="1">
          <stop offset="0" stopColor="#5eead4" />
          <stop offset="1" stopColor="#0d9488" />
        </linearGradient>
      </defs>
      <path d="M1 1h8l6 13 6-13h8L18 25h-6z" fill="url(#cn-mark-fill)" />
      <path d="M9 1l6 13" stroke="rgba(0,0,0,.25)" strokeWidth="1.2" />
    </svg>
  );
}
