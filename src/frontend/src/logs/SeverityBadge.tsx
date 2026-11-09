import type { Severity } from "../api/types";

/** The short label of each severity. "Information" is shortened, as in the mockups, to keep the column narrow. */
export const severityLabels: Record<Severity, string> = {
  Unspecified: "Unset",
  Trace: "Trace",
  Debug: "Debug",
  Information: "Info",
  Warning: "Warning",
  Error: "Error",
  Fatal: "Fatal",
};

/** The colour family of each severity, matching the classes in logs.css. */
const tones: Record<Severity, string> = {
  Unspecified: "debug",
  Trace: "debug",
  Debug: "debug",
  Information: "info",
  Warning: "warning",
  Error: "error",
  Fatal: "error",
};

/** Props for {@link SeverityBadge}. */
interface SeverityBadgeProps {
  /** The severity to show. */
  severity: Severity;
}

/** A coloured label for a severity. The text carries the meaning, so the colour is never the only signal. */
export function SeverityBadge({ severity }: SeverityBadgeProps) {
  return <span className={`severity severity-${tones[severity]}`}>{severityLabels[severity]}</span>;
}
