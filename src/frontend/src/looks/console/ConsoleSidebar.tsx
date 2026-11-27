import type { Ref } from "react";
import { Link, NavLink, useLocation, useSearchParams } from "react-router";

import type { Session } from "../../api/types";
import type { AlertCounts } from "../../app/alertCounts";
import { navFor, type NavItem } from "../../app/look";
import type { RouteId } from "../../app/routes";
import type { ShellStatus } from "../../app/shellContext";
import { carryTime } from "../../time/carryTime";
import { useAgentsSummary } from "../../app/useAgentsSummary";
import { ConsoleIcon, ConsoleMark, type ConsoleIconName } from "./ConsoleIcon";
import { consoleNav, consoleOwners } from "./nav";
import type { ConsoleForm } from "./useConsoleForm";

/** Props for {@link ConsoleSidebar}. */
interface ConsoleSidebarProps {
  /** The signed-in person, whose role decides which entries show. */
  session: Session;
  /** The shared status, for the foot's connection line. */
  status: ShellStatus;
  /** How to draw it. */
  form: ConsoleForm;
  /** In the drawer form, whether the drawer is open. */
  open: boolean;
  /** Closes the drawer. */
  onClose(): void;
  /** The drawer's close button, which gets the focus when the drawer opens. */
  closeRef: Ref<HTMLButtonElement>;
  /** The open alerts, counted beside Alerts; null before the first snapshot. */
  counts: AlertCounts | null;
  /** The version this page runs, at the foot; null before it is known (VEY-82). */
  version: string | null;
  /** Whether a newer version runs, which marks the version. */
  newer: boolean;
  /** Opens the release notes. */
  onNotes(): void;
}

/** Console's picture for each menu route. */
const icons: Partial<Record<RouteId, ConsoleIconName>> = {
  home: "overview",
  logs: "logs",
  traces: "traces",
  metrics: "metrics",
  services: "services",
  hosts: "hosts",
  databases: "database",
  jobs: "jobs",
  incidents: "incidents",
  alerts: "alerts",
  integrations: "integrations",
  keys: "keys",
  users: "users",
  notifications: "notifications",
  settings: "settings",
};

/**
 * The Console sidebar (VEY-69 mockups): the V mark and the name, the menu in its groups (Observe, Respond, Manage),
 * each entry marked when its screen is open, and at its foot whether Veyra is answering, for Admins how many agents
 * are connected, and the version, which opens the release notes. Labelled on desktops; icons with their names on hover
 * and focus on tablets; a drawer on phones.
 */
export function ConsoleSidebar({
  session,
  status,
  form,
  open,
  onClose,
  closeRef,
  counts,
  version,
  newer,
  onNotes,
}: ConsoleSidebarProps) {
  const groups = navFor({ nav: consoleNav }, session.role);
  const agents = useAgentsSummary(session.role === "Admin");
  const answering = status.error === null;

  return (
    <nav
      id="cn-sidebar"
      className={["cn-side", `cn-side-${form}`, form === "drawer" && open ? "open" : ""].filter(Boolean).join(" ")}
      aria-label="Main"
      inert={form === "drawer" && !open}
    >
      <div className="cn-brand-row">
        <Link to="/" className="cn-brand">
          <ConsoleMark />
          <span className="cn-word" aria-hidden="true">
            <b>Veyra</b>
            <small>Observability</small>
          </span>
          <span className="visually-hidden">Veyra home</span>
        </Link>
        {form === "drawer" && (
          <button ref={closeRef} type="button" className="cn-icon" aria-label="Close the menu" onClick={onClose}>
            <ConsoleIcon name="close" size={16} />
          </button>
        )}
      </div>
      {groups.map((group) => {
        const titleId = `cn-group-${(group.title ?? "main").toLowerCase()}`;

        return (
          <section key={titleId} className="cn-group" aria-labelledby={titleId}>
            <p id={titleId} className="cn-group-title">
              {group.title}
            </p>
            <ul className="cn-list">
              {group.items.map((item) => (
                <Entry key={item.route.id} item={item} counts={item.route.id === "alerts" ? counts : null} />
              ))}
            </ul>
          </section>
        );
      })}
      <div className="cn-foot">
        <p>
          <span className={answering ? "cn-dot ok" : "cn-dot bad"} aria-hidden="true" />
          <span className="cn-foot-text">{answering ? "Connected" : "Veyra is not answering"}</span>
        </p>
        {agents && (
          <p>
            <span className={agents.agentsOnline < agents.agents ? "cn-dot warn" : "cn-dot ok"} aria-hidden="true" />
            <span className="cn-foot-text">
              {agents.agentsOnline} of {agents.agents} {agents.agents === 1 ? "agent" : "agents"} connected
            </span>
          </p>
        )}
        {version && (
          <p>
            <button
              type="button"
              className={newer ? "cn-version newer" : "cn-version"}
              aria-label={`Veyra ${version}: what's new${newer ? ", a newer version is ready" : ""}`}
              onClick={onNotes}
            >
              <span className="cn-foot-text">Veyra </span>
              {version}
            </button>
          </p>
        )}
      </div>
    </nav>
  );
}

/** Props for {@link Entry}. */
interface EntryProps {
  /** The menu entry. */
  item: NavItem;
  /** The open alerts, for the Alerts entry only. */
  counts: AlertCounts | null;
}

/** One entry: its picture and name, and for Alerts the open warnings and cautions, also in words for screen readers. */
function Entry({ item, counts }: EntryProps) {
  const { route, label } = item;
  // Moving to another screen keeps the range on screen (ADR 0022).
  const [params] = useSearchParams();
  const describedBy = counts ? `cn-${route.id}-description` : undefined;
  // A detail screen Console files elsewhere marks its owner instead of the entry its address starts with.
  const { pathname } = useLocation();
  const owner = consoleOwners.find((one) => pathname.startsWith(one.prefix))?.route;
  const current = owner ? owner === route.id : undefined;

  const content = (
    <>
      <ConsoleIcon name={icons[route.id] ?? "overview"} className="cn-item-icon" />
      <span className="cn-label">{label}</span>
      {counts && counts.warning + counts.caution > 0 && (
        <span className="cn-counts" aria-hidden="true">
          {counts.warning > 0 && <em className="cn-count warning">{counts.warning}</em>}
          {counts.caution > 0 && <em className="cn-count caution">{counts.caution}</em>}
        </span>
      )}
    </>
  );

  return (
    <li>
      {current === undefined ? (
        <NavLink
          to={carryTime(route.path, params)}
          end={route.path === "/"}
          className="cn-item"
          aria-describedby={describedBy}
        >
          {content}
        </NavLink>
      ) : (
        <Link
          to={carryTime(route.path, params)}
          className={current ? "cn-item active" : "cn-item"}
          aria-current={current ? "page" : undefined}
          aria-describedby={describedBy}
        >
          {content}
        </Link>
      )}
      {counts && (
        <span id={describedBy} className="visually-hidden">
          {counts.words}
        </span>
      )}
    </li>
  );
}
