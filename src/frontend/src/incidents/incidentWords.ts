import type { IncidentItem, IncidentMoment } from "../api/types";
import { useSession } from "../session/useSession";

/*
 * The words and rules every look's incident screens share (ADR 0026).
 */

/** How an incident stands, in words. */
export function stateWords(incident: IncidentItem): string {
  if (incident.state === "resolved") {
    return "Resolved";
  }

  return incident.state === "acknowledged"
    ? `Acknowledged${incident.acknowledgedBy ? ` · ${incident.acknowledgedBy}` : ""}`
    : "Open";
}

/** What happened at a moment of an incident's own timeline, in words. */
export function momentWords(moment: IncidentMoment): string {
  switch (moment.kind) {
    case "opened":
      return "Alert opened";
    case "escalated":
      return "Alert got worse";
    case "eased":
      return "Alert eased";
    case "resolved":
      return "Alert resolved";
    case "acknowledged":
      return "Acknowledged";
    case "unacknowledged":
      return "Acknowledgement taken back";
  }
}

/** The affected blocks of an incident in one line: `orders-api · web-01`. */
export function blocksWords(incident: IncidentItem): string {
  return incident.blocks.map((block) => block.name.replace(/^[a-z.]+_server |^postgresql /, "")).join(" · ");
}

/**
 * Whether the signed-in person may acknowledge incidents: Editors and Admins (ADR 0008).
 *
 * @returns True for an Editor or Admin.
 */
export function useMayAcknowledge(): boolean {
  const { state } = useSession();

  return state.status === "signedIn" && (state.session.role === "Editor" || state.session.role === "Admin");
}
