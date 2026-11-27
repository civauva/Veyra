import { useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router";

import { ReleaseNotesDialog } from "../../about/ReleaseNotesDialog";
import { UpdateNotice } from "../../about/UpdateNotice";
import { useAbout } from "../../about/useAbout";
import type { Session } from "../../api/types";
import { alertCounts } from "../../app/alertCounts";
import type { ShellContext, ShellScene } from "../../app/shellContext";
import { useDrawer } from "../../app/useDrawer";
import { useShellFrame } from "../../app/useShellFrame";
import { useShellTime } from "../../app/useShellTime";
import { useSession } from "../../session/useSession";
import { NavIcon } from "../../ui/NavIcon";
import { AlertBadges, Brand, Sidebar } from "./Sidebar";
import { EnvironmentSwitcher } from "./EnvironmentSwitcher";
import { useBlendedNavigation } from "./useBlendedNavigation";
import { useSidebar } from "./useSidebar";
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
  const { status, times, theme, setTheme } = useShellFrame(session);
  const sidebar = useSidebar();
  const blend = useBlendedNavigation();
  const [override, setScene] = useState<ShellScene | null>(null);
  const location = useLocation();
  const {
    open: drawerOpen,
    show: showDrawer,
    close: closeDrawer,
    menuRef,
    closeRef,
  } = useDrawer(sidebar.form === "drawer");

  const kind = status.snapshot?.condition.kind;
  const scene = override ?? (kind ? (kind.toLowerCase() as ShellScene) : null);
  const calm = !(location.pathname === "/" || location.pathname.startsWith("/alerts"));
  const counts = alertCounts(status.snapshot);

  // The bar across the top of every screen, which each screen fills with ScreenBar (VEY-62).
  const [bar, setBar] = useState<HTMLDivElement | null>(null);
  const about = useAbout();
  const [notes, setNotes] = useState(false);
  const time = useShellTime();
  const context: ShellContext = { status, setScene, bar, time };
  const classes = ["shell", scene ? `scene scene-${scene}` : "", calm ? "calm" : ""].filter(Boolean).join(" ");

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
            onClick={showDrawer}
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
        version={about.version}
        newer={about.newer !== null}
        onNotes={() => setNotes(true)}
      />
      {drawerOpen && <div className="shell-scrim" aria-hidden="true" onClick={closeDrawer} />}
      <div className="shell-main">
        <div ref={setBar} className="screen-bar" />
        {/*
          A zone picked on the Account page changes the session, which draws every screen again. The UTC switch is the
          shell's own, so the screen starts over in it, as on a reload.
        */}
        <Outlet key={times.utc ? "utc" : "local"} context={context} />
        <UpdateNotice about={about} onNotes={() => setNotes(true)} />
        {notes && <ReleaseNotesDialog current={about.version} onClose={() => setNotes(false)} />}
      </div>
    </div>
  );
}
