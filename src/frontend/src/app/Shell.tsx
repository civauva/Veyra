import { useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router";

import type { Session } from "../api/types";
import { EnvironmentPills, MasterLights } from "../home/HomeStatus";
import { useSession } from "../session/useSession";
import { Logo } from "./Logo";
import type { ShellContext, ShellScene } from "./shellContext";
import { ThemeToggle } from "./ThemeToggle";
import { useBlendedNavigation } from "./useBlendedNavigation";
import { useShellStatus } from "./useShellStatus";
import { Weather } from "./Weather";
import "./shell.css";

/** Props for {@link Shell}. */
interface ShellProps {
  /** The signed-in user. */
  session: Session;
}

/**
 * The frame around every screen after sign-in (VEY-23): one top bar with the environment pills, the navigation, the
 * master lights, the clock, the theme, and the user's menu, the same on every screen. Navigation shows only what the
 * user's role allows (ADR 0008).
 *
 * The weather of the chosen environment lies behind the whole frame: in full on the home and the alert screens, as a
 * calm band under the bar on the work screens. Moving between screens crossfades them, so the home blends into the
 * next screen instead of switching. On phones the navigation, theme, and sign-out fold into a menu.
 */
export function Shell({ session }: ShellProps) {
  const { signOut } = useSession();
  const status = useShellStatus();
  const blend = useBlendedNavigation();
  const [override, setScene] = useState<ShellScene | null>(null);
  const [menu, setMenu] = useState({ open: false, path: "" });
  const location = useLocation();

  // The phone menu closes when a link in it leads somewhere else.
  const menuOpen = menu.open && menu.path === location.pathname;

  const kind = status.snapshot?.condition.kind;
  const scene = override ?? (kind ? (kind.toLowerCase() as ShellScene) : null);
  const calm = !(location.pathname === "/" || location.pathname.startsWith("/alerts"));

  const initials = session.displayName
    .split(/\s+/)
    .map((part) => part[0] ?? "")
    .join("")
    .slice(0, 2)
    .toUpperCase();

  const context: ShellContext = { status, setScene };
  const classes = ["shell", scene ? `scene scene-${scene}` : "", calm ? "calm" : ""].filter(Boolean).join(" ");

  return (
    <div className={classes}>
      <Weather scene={scene} calm={calm} />
      {scene === "clear" && <div className="shell-sun" aria-hidden="true" />}
      <div className="shell-veil" aria-hidden="true" />
      <header className="shell-bar">
        <span className="shell-brand">
          <Logo />
          <span className="shell-name">veyra</span>
        </span>
        <div className="shell-slot shell-slot-start">
          {status.snapshot && (
            <EnvironmentPills snapshot={status.snapshot} environment={status.environment} onChoose={status.choose} />
          )}
        </div>
        <span className="shell-spacer" />
        <div className="shell-slot shell-slot-end">
          {status.snapshot && <MasterLights snapshot={status.snapshot} />}
        </div>
        <button
          type="button"
          className="button shell-menu-toggle"
          aria-expanded={menuOpen}
          aria-controls="shell-menu"
          onClick={() => setMenu({ open: !menuOpen, path: location.pathname })}
        >
          <span aria-hidden="true">☰</span>
          <span className="visually-hidden">Menu</span>
        </button>
        <div id="shell-menu" className={menuOpen ? "shell-menu open" : "shell-menu"}>
          <nav className="shell-nav" aria-label="Main">
            <NavLink to="/" end onClick={(event) => blend(event, "/")}>
              Home
            </NavLink>
            <NavLink to="/logs" onClick={(event) => blend(event, "/logs")}>
              Logs
            </NavLink>
            <NavLink to="/metrics" onClick={(event) => blend(event, "/metrics")}>
              Metrics
            </NavLink>
            <NavLink to="/alerts" onClick={(event) => blend(event, "/alerts")}>
              Alerts
            </NavLink>
            {session.role === "Admin" && (
              <NavLink to="/keys" onClick={(event) => blend(event, "/keys")}>
                Keys
              </NavLink>
            )}
            {session.role === "Admin" && (
              <NavLink to="/users" onClick={(event) => blend(event, "/users")}>
                Users
              </NavLink>
            )}
            {session.role === "Admin" && (
              <NavLink to="/notifications" onClick={(event) => blend(event, "/notifications")}>
                Notifications
              </NavLink>
            )}
          </nav>
          <ThemeToggle />
          <NavLink
            to="/account"
            className="shell-user"
            title={`${session.displayName} · ${session.role}`}
            aria-label="Your account"
            onClick={(event) => blend(event, "/account")}
          >
            <span className="shell-user-initials">{initials}</span>
            <span className="shell-user-label">Your account</span>
          </NavLink>
          <button type="button" className="button shell-signout" onClick={() => void signOut()}>
            Sign out
          </button>
        </div>
      </header>
      <Outlet context={context} />
    </div>
  );
}
