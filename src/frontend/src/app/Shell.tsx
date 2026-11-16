import { useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router";

import { loadRuleCatalog } from "../alerts/ruleCatalog";
import type { Session } from "../api/types";
import { useSession } from "../session/useSession";
import { setDisplayZone } from "../time/zone";
import { EnvironmentSwitcher } from "./EnvironmentSwitcher";
import { NavIcon } from "./NavIcon";
import type { ShellContext, ShellScene } from "./shellContext";
import { AlertBadges, alertCounts, Brand, Sidebar } from "./Sidebar";
import { useTheme } from "./ThemeChoice";
import { useBlendedNavigation } from "./useBlendedNavigation";
import { useShellStatus } from "./useShellStatus";
import { useSidebar } from "./useSidebar";
import { useTimes } from "./useTimes";
import { Weather } from "./Weather";
import "./shell.css";

/** Props for {@link Shell}. */
interface ShellProps {
  /** The signed-in user. */
  session: Session;
}

/**
 * The frame around every screen after sign-in (VEY-26): the sidebar on the left and the screen beside it. The
 * sidebar carries the logo, the environment, the screens, the settings, and the person, the same on every screen;
 * navigation shows only what the user's role allows (ADR 0008). There is no top bar, so the screens get the full
 * height. On phones a slim bar keeps the menu button, the environment, and the open alerts, and the sidebar opens as
 * a drawer.
 *
 * The weather of the chosen environment lies behind the whole frame: in full on the home and the alert screens, as a
 * calm band at the top of the work screens. Moving between screens crossfades them, so the home blends into the next
 * screen instead of switching.
 */
export function Shell({ session }: ShellProps) {
  const { signOut } = useSession();
  const status = useShellStatus();
  const sidebar = useSidebar();
  const [theme, setTheme] = useTheme();
  const times = useTimes(session);

  // The catalog rules word their alerts from the API (ADR 0020); loaded once, quietly, for every screen.
  useEffect(() => {
    void loadRuleCatalog();
  }, []);

  // Every formatter reads the zone, so it is set before the screens below render (VEY-22).
  setDisplayZone(times.zone);
  const blend = useBlendedNavigation();
  const [override, setScene] = useState<ShellScene | null>(null);
  const [drawer, setDrawer] = useState({ open: false, path: "" });
  const location = useLocation();
  const menuRef = useRef<HTMLButtonElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  // The drawer closes when a link in it leads somewhere else, and whenever the window grows out of the drawer form.
  const drawerOpen = sidebar.form === "drawer" && drawer.open && drawer.path === location.pathname;

  useEffect(() => {
    if (!drawerOpen) {
      return undefined;
    }

    closeRef.current?.focus();

    function keyed(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setDrawer({ open: false, path: "" });
        menuRef.current?.focus();
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [drawerOpen]);

  const kind = status.snapshot?.condition.kind;
  const scene = override ?? (kind ? (kind.toLowerCase() as ShellScene) : null);
  const calm = !(location.pathname === "/" || location.pathname.startsWith("/alerts"));
  const counts = alertCounts(status.snapshot);

  const context: ShellContext = { status, setScene };
  const classes = ["shell", scene ? `scene scene-${scene}` : "", calm ? "calm" : ""].filter(Boolean).join(" ");

  function closeDrawer() {
    setDrawer({ open: false, path: "" });
    menuRef.current?.focus();
  }

  return (
    <div className={classes} data-sidebar={sidebar.form} data-zone={times.zone}>
      <Weather scene={scene} calm={calm} />
      {scene === "clear" && <div className="shell-sun" aria-hidden="true" />}
      <div className="shell-veil" aria-hidden="true" />
      {sidebar.form === "drawer" && (
        <header className="shell-top">
          <button
            ref={menuRef}
            type="button"
            className="shell-top-button"
            aria-label="Menu"
            aria-expanded={drawerOpen}
            aria-controls="shell-sidebar"
            onClick={() => setDrawer({ open: true, path: location.pathname })}
          >
            <NavIcon name="menu" size={22} />
          </button>
          <Brand compact />
          <EnvironmentSwitcher
            snapshot={status.snapshot}
            environment={status.environment}
            onChoose={status.choose}
            compact
          />
          <span className="shell-spacer" />
          <NavLink
            to="/alerts"
            className="shell-top-button shell-top-alerts"
            aria-describedby={counts ? "shell-top-alerts-description" : undefined}
            onClick={(event) => blend(event, "/alerts")}
          >
            <NavIcon name="alerts" size={22} />
            <span className="visually-hidden">Alerts</span>
            {counts && <AlertBadges warning={counts.warning} caution={counts.caution} />}
          </NavLink>
          {counts && (
            <span id="shell-top-alerts-description" className="visually-hidden">
              {counts.words}
            </span>
          )}
        </header>
      )}
      <Sidebar
        session={session}
        status={status}
        form={sidebar.form}
        open={drawerOpen}
        onToggle={sidebar.toggle}
        onClose={closeDrawer}
        closeRef={closeRef}
        theme={theme}
        onTheme={setTheme}
        times={times}
        onSignOut={() => void signOut()}
      />
      {drawerOpen && <div className="shell-scrim" aria-hidden="true" onClick={closeDrawer} />}
      <div className="shell-main">
        {/*
          A zone picked on the Account page changes the session, which draws every screen again. The UTC switch is the
          shell's own, so the screen starts over in it, as on a reload.
        */}
        <Outlet key={times.utc ? "utc" : "local"} context={context} />
      </div>
    </div>
  );
}
