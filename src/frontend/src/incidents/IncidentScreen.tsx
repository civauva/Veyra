import { Link, useParams } from "react-router";

import { alertTitle } from "../alerts/ruleWords";
import { formatClock } from "../logs/format";
import { ScreenBar } from "../ui/ScreenBar";
import { momentWords, stateWords, useMayAcknowledge } from "./incidentWords";
import { useIncident } from "./useIncidents";
import "./incidents.css";

/**
 * One incident (ADR 0026) as every look shows it by default: its state with Acknowledge, its alerts, and its
 * timeline. The Console look draws its own, with the context around it.
 */
export function IncidentScreen() {
  const { id = "" } = useParams();
  const view = useIncident(id);
  const mayAcknowledge = useMayAcknowledge();
  const details = view.details;

  if (view.notFound) {
    return (
      <main className="incidents">
        <ScreenBar trail={[{ label: "Incidents", to: "/incidents" }, { label: "Not found" }]} />
        <p>This incident does not exist, or it was deleted with its alerts.</p>
      </main>
    );
  }

  return (
    <main className="incidents">
      <ScreenBar trail={[{ label: "Incidents", to: "/incidents" }, { label: details?.incident.key ?? "…" }]} />
      {!details ? (
        <p>Reading the incident…</p>
      ) : (
        <>
          <h1>{details.incident.title}</h1>
          <p className="incidents-lead">
            <span className="mono">{details.incident.key}</span> · {stateWords(details.incident)} · opened{" "}
            {formatClock(details.incident.openedAt)}
          </p>
          {mayAcknowledge && details.incident.state !== "resolved" && (
            <button
              type="button"
              className="button"
              disabled={view.saving}
              onClick={() => view.setAcknowledged(details.incident.state !== "acknowledged")}
            >
              {details.incident.state === "acknowledged" ? "Take back the acknowledgement" : "Acknowledge"}
            </button>
          )}
          {view.error && (
            <p className="incidents-error" role="alert">
              {view.error}
            </p>
          )}
          <h2>Alerts</h2>
          <ul className="incidents-list">
            {details.alerts.map((alert) => (
              <li key={alert.id} className={alert.worst === "Warning" ? "warning" : "caution"}>
                <Link to={`/alerts/${encodeURIComponent(alert.id)}`}>
                  {alertTitle(
                    alert.rule,
                    alert.block,
                    alert.resolvedAt ? alert.peak : alert.value,
                    alert.resolvedAt !== null,
                  )}
                </Link>
              </li>
            ))}
          </ul>
          <h2>Timeline</h2>
          <ol className="incidents-timeline">
            {details.timeline.map((moment, index) => (
              <li key={index}>
                <span className="mono">{formatClock(moment.at)}</span> {momentWords(moment)}
                {moment.actor ? ` · ${moment.actor}` : ""}
              </li>
            ))}
          </ol>
        </>
      )}
    </main>
  );
}
