import { Link } from "react-router";

import type { AlertListItem, BlockKind } from "../api/types";
import { alertTitle } from "../alerts/ruleWords";
import { useOpenAlerts } from "./database/useOpenAlerts";
import { allDismissed, useDismissedAlerts } from "./dismissedAlerts";
import "./blockStatus.css";

/** Props for {@link BlockStatus}. */
interface BlockStatusProps {
  /** Whether the screen is about a host, a service, or a database server. */
  kind: BlockKind;
  /** Its block name; for a database server, its engine and server. */
  name: string;
  /** The name in words, when it differs from the block name, such as `SQL Server sql-prod-01`. */
  label?: string;
  /**
   * The block's open alerts when the screen has read them already, so they are read once for the whole screen; null
   * while they are read or when they could not be. Left out, the banner reads them itself.
   */
  alerts?: AlertListItem[] | null;
}

/**
 * One sentence on how a host or service is, from its open alerts: the alert rules decide, never the screen (ADR 0007).
 * The first thing on its screen, in both views (VEY-37). A banner about alerts can be dismissed; it stays closed until
 * an alert opens that was not open then, or one of them changes severity (VEY-62). On a wide window the rail beside
 * the screen lists the same alerts, and the banner gives way to it (VEY-63).
 */
export function BlockStatus({ kind, name, label, alerts }: BlockStatusProps) {
  const block = `${kind}:${name}`;

  return alerts === undefined ? (
    <ReadingStatus key={block} kind={kind} name={name} label={label} />
  ) : (
    <StatusLine key={block} block={block} name={name} label={label} open={alerts} />
  );
}

/** The banner of a screen that has not read its alerts itself. */
function ReadingStatus({ kind, name, label }: Omit<BlockStatusProps, "alerts">) {
  const { alerts: open } = useOpenAlerts(kind, name);

  return <StatusLine block={`${kind}:${name}`} name={name} label={label} open={open} />;
}

/** Props for {@link StatusLine}. */
interface StatusLineProps {
  /** The block's kind and name, which its dismissal is remembered by. */
  block: string;
  /** Its block name. */
  name: string;
  /** Its name in words. */
  label?: string;
  /** Its open alerts, or null when unknown. */
  open: AlertListItem[] | null;
}

/** The sentence itself, with the button that dismisses it. */
function StatusLine({ block, name, label, open }: StatusLineProps) {
  const [dismissed, dismiss] = useDismissedAlerts(block);

  // Without alerts, the sentence is left out rather than claiming all is well.
  if (open === null || allDismissed(open, dismissed)) {
    return null;
  }

  // The server sends the open alerts the worst first (VEY-68).
  const worst = open[0];

  return (
    <p className={`block-status ${worst ? worst.severity.toLowerCase() : "ok"}`} role="status">
      <i aria-hidden="true" />
      {worst ? (
        <>
          <span>
            <b>{alertTitle(worst.rule, worst.block, worst.value, false)}.</b>
            {open.length > 1 && ` ${open.length - 1} more ${open.length === 2 ? "alert is" : "alerts are"} open.`}
          </span>
          <Link to={`/alerts/${encodeURIComponent(worst.id)}`}>Alert details</Link>
          <button
            type="button"
            className="block-status-close"
            aria-label="Dismiss"
            title="Dismiss"
            onClick={() => dismiss(open)}
          >
            <span aria-hidden="true">×</span>
          </button>
        </>
      ) : (
        <span>
          <b>{label ?? name} is fine.</b> No alert is open.
        </span>
      )}
    </p>
  );
}
