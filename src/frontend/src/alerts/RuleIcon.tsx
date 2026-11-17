import type { AlertRule } from "../api/types";

/** Props for {@link RuleIcon}. */
interface RuleIconProps {
  /** The rule to draw. */
  rule: AlertRule;
  /** The width and height in pixels. */
  size: number;
}

/**
 * A small line drawing for each built-in rule, beside its name: a log page for error rate, a stopwatch for latency, a
 * disk for disk space, a disk with a rising line for disk filling, a box for each container rule, and a database with
 * a mark of its own for each database rule. Decorative; the name beside it says the rule.
 */
export function RuleIcon({ rule, size }: RuleIconProps) {
  return (
    <svg
      className="rule-glyph"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {rule === "error-rate" && <path d="M5 5h14v14H5z M8 9h8 M8 12h5 M8 15h7" />}
      {rule === "latency-p95" && (
        <>
          <circle cx="12" cy="13" r="7.5" />
          <path d="M12 13l3.5-3.5 M10 3h4" />
        </>
      )}
      {rule === "disk-space" && (
        <>
          <rect x="3.5" y="6" width="17" height="12" rx="2" />
          <path d="M6.5 12h8" strokeWidth="3" />
        </>
      )}
      {rule === "disk-filling" && (
        <>
          <rect x="3.5" y="6" width="17" height="12" rx="2" />
          <path d="M7 14l3-3 2.5 2L17 9" />
        </>
      )}
      {rule.startsWith("container-") && <path d="M2.5 6.5l6-3 6 3v7l-6 3-6-3z M2.5 6.5l6 3 6-3 M8.5 9.5v7" />}
      {rule === "container-restarts" && <path d="M15.5 17.5a3 3 0 1 0 1-2.2 M16.3 13.6v1.7H18" />}
      {rule === "container-memory" && <path d="M15.5 15h5.5v5h-5.5z M17.3 15v-1.5 M19.3 15v-1.5" />}
      {rule.startsWith("db-") && (
        <path d="M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3z M4 6v12c0 1.7 3.6 3 8 3" />
      )}
      {rule === "db-blocking" && <path d="M15 14h6v6h-6z M17 14v-1.5a1 1 0 0 1 2 0V14" />}
      {rule === "db-deadlocks" && <path d="M14.5 15.5l5 5 M19.5 15.5l-5 5" />}
      {rule === "db-long-query" && (
        <>
          <circle cx="18" cy="17" r="3.5" />
          <path d="M18 15.5V17l1 1" />
        </>
      )}
      {rule === "db-backup-age" && <path d="M14.5 17a3.5 3.5 0 1 0 1-2.5 M14.5 13.5v1.5h1.5" />}
      {rule === "db-agent-job" && <path d="M15 15l5 5 M20 15l-5 5" />}
      {rule === "db-connections" && (
        <>
          <circle cx="17" cy="14.2" r="1.7" />
          <path d="M14 20.5v-1.5a3 3 0 0 1 6 0v1.5" />
        </>
      )}
      {rule === "db-wraparound" && <path d="M14.5 17.5a3.5 3.5 0 1 1 3.5 3.5 M14.5 21v-3.5H18" />}
    </svg>
  );
}
