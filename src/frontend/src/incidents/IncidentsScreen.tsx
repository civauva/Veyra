import { useState } from "react";
import { Link } from "react-router";

import type { IncidentState } from "../api/types";
import { useShell } from "../app/shellContext";
import { formatClock } from "../logs/format";
import { ScreenBar } from "../ui/ScreenBar";
import { blocksWords, stateWords } from "./incidentWords";
import { useIncidents } from "./useIncidents";
import "./incidents.css";

/** The state filters, as the server takes them. */
const states: [IncidentState | "all", string][] = [
  ["open", "Open"],
  ["acknowledged", "Acknowledged"],
  ["resolved", "Resolved"],
  ["all", "All"],
];

/**
 * The incidents (ADR 0026) as every look shows them by default: open first, then newest, filtered on the server. The
 * Console look draws its own.
 */
export function IncidentsScreen() {
  const { status } = useShell();
  const [state, setState] = useState<IncidentState | "all">("all");
  const list = useIncidents(state, 30, status.environment);

  return (
    <main className="incidents">
      <ScreenBar trail={[{ label: "Incidents" }]} />
      <h1>Incidents</h1>
      <p className="incidents-lead">Related alerts grouped into one thing to work on.</p>
      <div className="seg" role="group" aria-label="State">
        {states.map(([value, label]) => (
          <button key={value} type="button" aria-pressed={state === value} onClick={() => setState(value)}>
            {label}
          </button>
        ))}
      </div>
      {list.error && (
        <p className="incidents-error" role="alert">
          Could not refresh: {list.error}
        </p>
      )}
      {list.items === null ? (
        <p>Reading the incidents…</p>
      ) : list.items.length === 0 ? (
        <p>No incident in the last 30 days.</p>
      ) : (
        <ul className="incidents-list">
          {list.items.map((incident) => (
            <li key={incident.id} className={incident.severity === "Warning" ? "warning" : "caution"}>
              <Link to={`/incidents/${encodeURIComponent(incident.id)}`}>
                <span className="mono">{incident.key}</span> {incident.title}
              </Link>
              <small>
                {blocksWords(incident)} · opened {formatClock(incident.openedAt)} · {stateWords(incident)}
              </small>
            </li>
          ))}
        </ul>
      )}
      {list.hasMore && (
        <button type="button" className="link-button" onClick={list.loadMore}>
          Show older incidents
        </button>
      )}
    </main>
  );
}
