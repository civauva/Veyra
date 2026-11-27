// The server sends them the worst and then the oldest first (VEY-68).
import type { ReactNode } from "react";
import { Link } from "react-router";

import type { AlertListItem, BlockKind } from "../api/types";
import { formatDuration } from "../alerts/alertFormat";
import { titleOnScreen } from "../alerts/ruleWords";
import { HelpTip } from "../ui/HelpTip";
import { allTabs } from "./database/tabs";
import { allDismissed, useDismissedAlerts } from "./dismissedAlerts";
import "./blockPage.css";

/** How many alerts the rail lists; the rest are a link away, on the Alerts screen. */
const shownAlerts = 6;

/** Props for {@link BlockPage}. */
export interface BlockPageProps {
  /** Whether the screen is about a host, a service, or a database server. */
  kind: BlockKind;
  /** Its block name; for a database server, its engine and server. */
  name: string;
  /** Its open alerts, or null while they are read or when they could not be. */
  alerts: AlertListItem[] | null;
  /** The screen's content: its heading, banner, tabs, and panels. */
  children: ReactNode;
}

/**
 * The content of a host, service, or database server screen, with the open alerts beside it on a wide window
 * (VEY-63). From 1800 px the spare width holds a rail listing the alerts, and the banner gives way to it; narrower,
 * the rail is not shown and the screen is as it was. The rail holds the alerts only, so nothing is lost without it.
 */
export function BlockPage({ kind, name, alerts, children }: BlockPageProps) {
  return (
    <div className="block-page">
      <div className="block-main">{children}</div>
      {alerts !== null && <AlertRail kind={kind} name={name} alerts={alerts} />}
    </div>
  );
}

/** Props for {@link AlertRail}. */
interface AlertRailProps {
  /** The block's kind. */
  kind: BlockKind;
  /** Its block name. */
  name: string;
  /** Its open alerts. */
  alerts: AlertListItem[];
}

/**
 * The rail: the open alerts, the worst and then the oldest first, each leading to its details. It hides with the
 * banner, until an alert opens that was not open then.
 */
function AlertRail({ kind, name, alerts }: AlertRailProps) {
  const [dismissed, dismiss] = useDismissedAlerts(`${kind}:${name}`);

  if (alerts.length === 0 || allDismissed(alerts, dismissed)) {
    return null;
  }

  // The server sends them the worst and then the oldest first (VEY-68).
  const list = `/alerts?state=open&q=${encodeURIComponent(name)}`;
  const rest = alerts.length - shownAlerts;

  return (
    <aside className="alert-rail" aria-label="Open alerts">
      <div className="alert-rail-head">
        <h2>
          Open alerts <b>{alerts.length}</b>
        </h2>
        <HelpTip label="the open alerts">
          The alerts open on this {kind === "database" ? "server" : kind} now, the worst and then the oldest first. Each
          opens its details: what it means and how to fix it. Hiding the list hides the banner too, until an alert opens
          that was not open then.
        </HelpTip>
        <Link to={list}>All alerts</Link>
        <button
          type="button"
          className="alert-rail-close"
          aria-label="Hide the open alerts"
          title="Hide the open alerts"
          onClick={() => dismiss(alerts)}
        >
          <span aria-hidden="true">×</span>
        </button>
      </div>
      <ol>
        {alerts.slice(0, shownAlerts).map((alert) => (
          <li key={alert.id}>
            <Link to={`/alerts/${encodeURIComponent(alert.id)}`} className={alert.severity.toLowerCase()}>
              <i aria-hidden="true" />
              <span>{titleOnScreen(alert)}</span>
              <small>
                {kind === "database" ? `${areaLabel(alert)} · ` : ""}open{" "}
                {formatDuration(Date.parse(alert.updatedAt) - Date.parse(alert.openedAt))}
              </small>
            </Link>
          </li>
        ))}
      </ol>
      {rest > 0 && (
        <Link to={list} className="alert-rail-more">
          {rest} more
        </Link>
      )}
    </aside>
  );
}

/** The name of the server tab an alert belongs to, such as `Databases`. */
function areaLabel(alert: AlertListItem): string {
  // The server tells each database alert's area (VEY-68).
  const area = alert.area ?? "server";

  return allTabs.find((tab) => tab.id === area)?.label ?? "Server";
}
