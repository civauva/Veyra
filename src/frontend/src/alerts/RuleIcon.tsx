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
 * disk for disk space, and a disk with a rising line for disk filling. Decorative; the name beside it says the rule.
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
    </svg>
  );
}
