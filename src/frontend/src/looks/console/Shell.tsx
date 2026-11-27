import { useEffect, useState } from "react";
import { NavLink, Outlet } from "react-router";

import type { Session } from "../../api/types";
import { alertCounts } from "../../app/alertCounts";
import type { ShellContext, ShellScene } from "../../app/shellContext";
import { useDrawer } from "../../app/useDrawer";
import { useShellFrame } from "../../app/useShellFrame";
import { useLiveRate } from "../../app/useLiveRate";
import { useShellTime } from "../../app/useShellTime";
import { useSession } from "../../session/useSession";
import { useAbout } from "../../about/useAbout";
import { ReleaseNotesDialog } from "../../about/ReleaseNotesDialog";
import { UpdateNotice } from "../../about/UpdateNotice";
import { ConsoleAccount } from "./ConsoleAccount";
import { ConsoleEnvironment } from "./ConsoleEnvironment";
import { ConsoleHelp, ShortcutsDialog } from "./ConsoleHelp";
import { ConsoleIcon } from "./ConsoleIcon";
import { typing } from "./ConsoleMenu";
import { ConsoleSearch } from "./ConsoleSearch";
import { ConsoleRefresh, ConsoleTime } from "./ConsoleTime";
import { ConsoleSidebar } from "./ConsoleSidebar";
import { useConsoleForm } from "./useConsoleForm";
import "./console.css";

/** Props for {@link Shell}. */
interface ShellProps {
  /** The signed-in person. */
  session: Session;
}

/**
 * The Console frame (VEY-69 mockups): the grouped sidebar on the left; across the top the environment, the search,
 * the time range, Simple and Advanced, the open alerts, and the person; and the screen below, which scrolls on its own
 * so the frame stays put. The top bar is the one place to change the time range, so screens leave out range choices
 * of their own; on phones the Simple and Advanced toggle stays in the screen's bar, where there is room for it.
 *
 * Console paints no weather: the condition a screen is about shows in the screen itself, so a scene a screen asks for
 * is accepted and left unpainted.
 *
 * It sets `data-look="console"` on the page while it is drawn, which switches the colour tokens to Console's.
 */
/** Where Console files the screens of a host, a service, and a database server: under its resource lists. */
const trailParents: ShellContext["trailParents"] = {
  host: { label: "Hosts & containers", to: "/resources/hosts" },
  service: { label: "Services", to: "/resources/services" },
  database: { label: "Databases", to: "/resources/databases" },
};

export function Shell({ session }: ShellProps) {
  const { signOut } = useSession();
  const { status, times, theme, setTheme } = useShellFrame(session);
  const form = useConsoleForm();
  const { open: drawerOpen, show, close, menuRef, closeRef } = useDrawer(form === "drawer");
  const [, setScene] = useState<ShellScene | null>(null);
  const [bar, setBar] = useState<HTMLDivElement | null>(null);
  const [viewSlot, setViewSlot] = useState<HTMLDivElement | null>(null);
  const time = useShellTime();
  const context: ShellContext = {
    status,
    setScene,
    bar,
    time,
    timeInBar: true,
    viewSlot: form === "drawer" ? null : viewSlot,
    viewSwitch: true,
    trailParents,
  };
  const counts = alertCounts(status.snapshot);
  const rate = useLiveRate(time.live, status.environment);
  const [shortcuts, setShortcuts] = useState(false);
  const about = useAbout();
  const [notes, setNotes] = useState(false);
  const { live, setLive } = time;

  // L switches Live, and ? shows the shortcuts, from anywhere but a field.
  useEffect(() => {
    function keyed(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey || event.altKey || typing(event)) {
        return;
      }

      if (event.key.toLowerCase() === "l") {
        setLive(!live);
      } else if (event.key === "?") {
        setShortcuts(true);
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [live, setLive]);

  useEffect(() => {
    document.documentElement.dataset.look = "console";

    return () => {
      delete document.documentElement.dataset.look;
    };
  }, []);

  return (
    <div className="cn" data-form={form} data-zone={times.zone}>
      <ConsoleSidebar
        session={session}
        status={status}
        form={form}
        open={drawerOpen}
        onClose={close}
        closeRef={closeRef}
        counts={counts}
        version={about.version}
        newer={about.newer !== null}
        onNotes={() => setNotes(true)}
      />
      {drawerOpen && <div className="cn-scrim" aria-hidden="true" onClick={close} />}
      <div className="cn-main">
        <header className="cn-top">
          {form === "drawer" && (
            <button
              ref={menuRef}
              type="button"
              className="cn-icon"
              aria-label="Menu"
              aria-expanded={drawerOpen}
              aria-controls="cn-sidebar"
              onClick={show}
            >
              <ConsoleIcon name="menu" size={18} />
            </button>
          )}
          <ConsoleEnvironment snapshot={status.snapshot} environment={status.environment} onChoose={status.choose} />
          <ConsoleSearch environment={status.environment} time={time.choice} />
          <ConsoleTime time={time} zone={times.zone} />
          <ConsoleRefresh time={time} />
          <button
            type="button"
            className={time.live ? "cn-control cn-live on" : "cn-control cn-live"}
            aria-pressed={time.live}
            title={
              rate
                ? `${rateWords(rate.logs)} logs, ${rateWords(rate.spans)} spans, ${rateWords(rate.points)} points per second`
                : undefined
            }
            onClick={() => time.setLive(!time.live)}
          >
            {time.live ? <span className="cn-dot ok" aria-hidden="true" /> : <ConsoleIcon name="play" size={12} />}
            Live
            {time.live && rate && (
              <span className="cn-live-rate">· {rateWords(rate.logs + rate.spans + rate.points)}/s</span>
            )}
          </button>
          {form !== "drawer" && <div ref={setViewSlot} className="cn-view-slot" />}
          <NavLink
            to="/alerts"
            className="cn-icon cn-bell"
            aria-describedby={counts ? "cn-bell-description" : undefined}
          >
            <ConsoleIcon name="alerts" size={16} />
            <span className="visually-hidden">Open alerts</span>
            {counts && counts.warning + counts.caution > 0 && (
              <em className={counts.warning > 0 ? "cn-badge warning" : "cn-badge caution"} aria-hidden="true">
                {counts.warning + counts.caution}
              </em>
            )}
          </NavLink>
          {counts && (
            <span id="cn-bell-description" className="visually-hidden">
              {counts.words}
            </span>
          )}
          <ConsoleHelp onShortcuts={() => setShortcuts(true)} />
          <ConsoleAccount
            session={session}
            theme={theme}
            onTheme={setTheme}
            times={times}
            onSignOut={() => void signOut()}
          />
        </header>
        <div ref={setBar} className="screen-bar" />
        {/* As in every look: the UTC switch starts the screen over in the other zone, as on a reload. */}
        <Outlet key={times.utc ? "utc" : "local"} context={context} />
        {shortcuts && <ShortcutsDialog onClose={() => setShortcuts(false)} />}
        <UpdateNotice about={about} onNotes={() => setNotes(true)} />
        {notes && <ReleaseNotesDialog current={about.version} onClose={() => setNotes(false)} />}
      </div>
    </div>
  );
}

/**
 * A rate in short words: whole numbers from 10 up, one decimal below.
 *
 * @param perSecond - Events per second.
 * @returns The rate, such as "248" or "3.1".
 */
function rateWords(perSecond: number): string {
  return perSecond >= 10 ? Math.round(perSecond).toLocaleString("en") : perSecond.toFixed(1);
}
