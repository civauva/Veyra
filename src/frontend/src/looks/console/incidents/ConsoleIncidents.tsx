import { useState } from "react";
import { Link } from "react-router";

import type { IncidentItem, IncidentState } from "../../../api/types";
import { useShell } from "../../../app/shellContext";
import { blocksWords } from "../../../incidents/incidentWords";
import { useIncidents } from "../../../incidents/useIncidents";
import { formatClock } from "../../../logs/format";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { SqlFigure, timeWords } from "../database/sqlParts";
import "./consoleIncidents.css";

/** The state filters, as the server takes them. */
const states: [IncidentState | "all", string][] = [
  ["open", "Open"],
  ["acknowledged", "Acknowledged"],
  ["resolved", "Resolved"],
  ["all", "All"],
];

/**
 * Console's incidents (VEY-76, ADR 0026): figures for the open ones and the last 30 days, then every incident, open
 * first and newest after, with a filter by state. The server groups, orders, and filters them.
 */
export function ConsoleIncidents() {
  const { status } = useShell();
  const [state, setState] = useState<IncidentState | "all">("all");
  const list = useIncidents(state, 30, status.environment);
  const items = list.items ?? [];
  const open = items.filter((one) => one.state !== "resolved");
  const acknowledged = open.filter((one) => one.state === "acknowledged");
  const warnings = open.filter((one) => one.severity === "Warning").length;
  const resolved = items.filter((one) => one.resolvedAt !== null);
  const medianResolve = median(
    resolved.map((one) => (Date.parse(one.resolvedAt ?? one.openedAt) - Date.parse(one.openedAt)) / 1000),
  );

  return (
    <main className="cn-screen in">
      <ScreenBar trail={[{ label: "Incidents" }]} />
      <div className="cn-head">
        <div>
          <h1>
            Incidents
            {open.length > 0 && (
              <span className={warnings > 0 ? "cn-pill bad" : "cn-pill warn"}>{open.length} open</span>
            )}
          </h1>
          <p>Related alerts grouped into one thing to work on. The server groups them by the blocks they hit.</p>
        </div>
      </div>

      {state === "all" && list.items && (
        <section className="sq-figures sq-figures-five" aria-label="Figures">
          <SqlFigure
            label="Open"
            value={open.length}
            detail={`${warnings} warning · ${open.length - warnings} caution`}
            tone={warnings > 0 ? "bad" : open.length > 0 ? "warn" : undefined}
            help="Incidents with an alert still open, by their worst severity."
          />
          <SqlFigure
            label="Acknowledged"
            value={
              <>
                {acknowledged.length}
                <small> / {open.length}</small>
              </>
            }
            detail="of the open ones"
            help="Open incidents someone said they work on. Acknowledging does not stop notifications yet."
          />
          <SqlFigure
            label="Last 30 days"
            value={items.length}
            detail={`${resolved.length} resolved`}
            help="Every incident that was open at some time in the last 30 days."
          />
          <SqlFigure
            label="Alerts in them"
            value={items.reduce((sum, one) => sum + one.alertCount, 0)}
            detail="grouped by related blocks"
            help="Alerts the incidents hold. Alerts on related blocks within minutes of each other join one incident."
          />
          <SqlFigure
            label="Time to resolve"
            value={timeWords(medianResolve)}
            detail="median"
            help="Half of the resolved incidents closed faster than this."
          />
        </section>
      )}

      {list.error && (
        <p className="cn-error" role="alert">
          Could not refresh: {list.error}
        </p>
      )}

      <section className="cn-card" aria-labelledby="in-list">
        <header>
          <h2 id="in-list">Incidents</h2>
          <HelpTip label="Incidents">
            Every incident of the last 30 days, open ones first and the newest after. Open one for its timeline, the
            charts and logs around it, and what to check.
          </HelpTip>
          <div className="cn-seg in-states" role="group" aria-label="State">
            {states.map(([value, label]) => (
              <button key={value} type="button" aria-pressed={state === value} onClick={() => setState(value)}>
                {label}
              </button>
            ))}
          </div>
        </header>
        {list.items === null ? (
          <p className="cn-empty">Reading the incidents…</p>
        ) : items.length === 0 ? (
          <p className="cn-empty">No incident matches.</p>
        ) : (
          <div className="cn-table-scroll">
            <table className="cn-table">
              <thead>
                <tr>
                  <th scope="col">Incident</th>
                  <th scope="col">Severity</th>
                  <th scope="col">Title</th>
                  <th scope="col" className="wide">
                    Affected
                  </th>
                  <th scope="col" className="num wide">
                    Alerts
                  </th>
                  <th scope="col" className="num">
                    Opened
                  </th>
                  <th scope="col" className="num wide">
                    Lasted
                  </th>
                  <th scope="col">State</th>
                </tr>
              </thead>
              <tbody>
                {items.map((incident) => (
                  <Row key={incident.id} incident={incident} />
                ))}
              </tbody>
            </table>
          </div>
        )}
        {list.hasMore && (
          <footer>
            <button type="button" className="link-button" onClick={list.loadMore}>
              Load older →
            </button>
          </footer>
        )}
      </section>
    </main>
  );
}

/** One incident in the table. */
function Row({ incident }: { incident: IncidentItem }) {
  const end = incident.resolvedAt ?? new Date(Date.parse(incident.openedAt)).toISOString();
  const lasted = incident.resolvedAt ? (Date.parse(end) - Date.parse(incident.openedAt)) / 1000 : null;

  return (
    <tr>
      <td className="mono">{incident.key}</td>
      <td>
        <span
          className={
            incident.state === "resolved"
              ? "cn-pill off"
              : incident.severity === "Warning"
                ? "cn-pill bad"
                : "cn-pill warn"
          }
        >
          {incident.severity}
        </span>
      </td>
      <td>
        <Link className="cn-table-name" to={`/incidents/${encodeURIComponent(incident.id)}`}>
          {incident.title}
        </Link>
      </td>
      <td className="wide sq-muted">{blocksWords(incident)}</td>
      <td className="num wide">{incident.alertCount}</td>
      <td className="num">{formatClock(incident.openedAt)}</td>
      <td className="num wide">{lasted === null ? "ongoing" : timeWords(lasted)}</td>
      <td>
        <span
          className={
            incident.state === "resolved"
              ? "cn-pill off"
              : incident.state === "acknowledged"
                ? "cn-pill info"
                : "cn-pill bad"
          }
        >
          {incident.state === "resolved"
            ? "Resolved"
            : incident.state === "acknowledged"
              ? `Acknowledged${incident.acknowledgedBy ? ` · ${incident.acknowledgedBy}` : ""}`
              : "Open"}
        </span>
      </td>
    </tr>
  );
}

/** The median of some numbers, or null without any. */
function median(values: number[]): number | null {
  if (values.length === 0) {
    return null;
  }

  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);

  return sorted.length % 2 ? (sorted[middle] ?? null) : ((sorted[middle - 1] ?? 0) + (sorted[middle] ?? 0)) / 2;
}
