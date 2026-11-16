import type { ReactNode, Ref } from "react";
import { Link, NavLink } from "react-router";

import type { HomeSnapshot, Session } from "../api/types";
import { AccountMenu } from "./AccountMenu";
import { EnvironmentSwitcher } from "./EnvironmentSwitcher";
import { Logo } from "./Logo";
import { NavIcon, type NavIconName } from "./NavIcon";
import type { ShellStatus } from "./shellContext";
import type { ThemeName } from "./ThemeChoice";
import type { Times } from "./useTimes";
import { useBlendedNavigation } from "./useBlendedNavigation";
import type { SidebarForm } from "./useSidebar";

/** Props for {@link Sidebar}. */
interface SidebarProps {
  /** The signed-in person. */
  session: Session;
  /** The shared status, for the environment and the open alerts. */
  status: ShellStatus;
  /** Which form to draw. */
  form: SidebarForm;
  /** In the drawer form, whether the drawer is open. */
  open: boolean;
  /** Switches between labelled and icons. */
  onToggle(): void;
  /** Closes the drawer. */
  onClose(): void;
  /** The drawer's close button, which gets the focus when the drawer opens. */
  closeRef: Ref<HTMLButtonElement>;
  /** The theme in this browser. */
  theme: ThemeName;
  /**
   * Called with the theme the person picks.
   *
   * @param choice - The theme.
   */
  onTheme(choice: ThemeName): void;
  /** The zone times are shown in, with the switch to UTC. */
  times: Times;
  /** Signs the person out. */
  onSignOut(): void;
}

/**
 * The left sidebar (VEY-26), the same on every screen: the logo, which leads home; the environment every screen
 * shows; the screens, each marked in its own colour when it is open, with the open alerts counted beside Alerts;
 * the settings for administrators (ADR 0008); and the person, with their account, the theme, and signing out.
 *
 * Labelled on large screens, icons on laptops and tablets, and a drawer on phones (see {@link SidebarForm}).
 */
export function Sidebar({
  session,
  status,
  form,
  open,
  onToggle,
  onClose,
  closeRef,
  theme,
  onTheme,
  times,
  onSignOut,
}: SidebarProps) {
  const counts = alertCounts(status.snapshot);

  return (
    <nav
      id="shell-sidebar"
      className={["sb", `sb-${form}`, form === "drawer" && open ? "open" : ""].filter(Boolean).join(" ")}
      aria-label="Main"
      inert={form === "drawer" && !open}
    >
      <div className="sb-top">
        <Brand />
        {form === "full" && (
          <button type="button" className="sb-icon-button" aria-label="Collapse the sidebar" onClick={onToggle}>
            <NavIcon name="collapse" size={18} />
          </button>
        )}
        {form === "drawer" && (
          <button ref={closeRef} type="button" className="sb-icon-button" aria-label="Close the menu" onClick={onClose}>
            <NavIcon name="close" size={18} />
          </button>
        )}
      </div>
      <EnvironmentSwitcher snapshot={status.snapshot} environment={status.environment} onChoose={status.choose} />
      <ul className="sb-list">
        <Item to="/" end label="Home" icon="home" />
        <Item to="/logs" label="Logs" icon="logs" />
        <Item to="/traces" label="Traces" icon="traces" />
        <Item to="/metrics" label="Metrics" icon="metrics" />
        <Item to="/alerts" label="Alerts" icon="alerts" description={counts?.words}>
          {counts && <AlertBadges warning={counts.warning} caution={counts.caution} />}
        </Item>
      </ul>
      <span className="sb-grow" />
      {session.role === "Admin" && (
        <section className="sb-admin" aria-labelledby="sb-admin-title">
          <p id="sb-admin-title" className="sb-cap">
            Admin
          </p>
          <ul className="sb-list">
            <Item to="/keys" label="Ingestion keys" icon="keys" />
            <Item to="/users" label="Users" icon="users" />
            <Item to="/notifications" label="Notifications" icon="notifications" />
            <Item to="/settings" label="Settings" icon="settings" />
          </ul>
        </section>
      )}
      <AccountMenu session={session} theme={theme} onTheme={onTheme} times={times} onSignOut={onSignOut} />
      {form === "mini" && (
        <button type="button" className="sb-icon-button sb-expand" aria-label="Expand the sidebar" onClick={onToggle}>
          <NavIcon name="expand" size={18} />
        </button>
      )}
    </nav>
  );
}

/** The logo and the name, leading home: the first place people look for a way back. */
export function Brand({ compact = false }: { compact?: boolean }) {
  const blend = useBlendedNavigation();

  return (
    <Link to="/" className={compact ? "sb-brand compact" : "sb-brand"} onClick={(event) => blend(event, "/")}>
      <Logo />
      {!compact && (
        <span className="sb-name" aria-hidden="true">
          veyra
        </span>
      )}
      <span className="visually-hidden">Veyra home</span>
    </Link>
  );
}

/** Props for {@link Item}. */
interface ItemProps {
  /** Where it leads. */
  to: string;
  /** Marked current only on this exact path, as the home must be. */
  end?: boolean;
  /** The screen's name. */
  label: string;
  /** Its picture, which also picks its colour. */
  icon: NavIconName;
  /** More for screen readers, such as the open alerts beside Alerts. */
  description?: string;
  /** Drawn after the name, such as the alert badges. */
  children?: ReactNode;
}

/**
 * One screen in the sidebar. In the icon form the name stays in the link for screen readers and shows as a label on
 * hover and focus.
 */
function Item({ to, end = false, label, icon, description, children }: ItemProps) {
  const blend = useBlendedNavigation();
  const describedBy = description ? `sb-${icon}-description` : undefined;

  return (
    <li>
      <NavLink
        to={to}
        end={end}
        className={`sb-item sb-${icon}`}
        aria-describedby={describedBy}
        onClick={(event) => blend(event, to)}
      >
        <NavIcon name={icon} className="sb-item-icon" />
        <span className="sb-label">{label}</span>
        {children}
      </NavLink>
      {description && (
        <span id={describedBy} className="visually-hidden">
          {description}
        </span>
      )}
    </li>
  );
}

/** Props for {@link AlertBadges}. */
interface AlertBadgesProps {
  /** Open warnings. */
  warning: number;
  /** Open cautions. */
  caution: number;
}

/** Red and amber counts, as the master lights were: red is warning and amber caution on every screen (ADR 0007). */
export function AlertBadges({ warning, caution }: AlertBadgesProps) {
  if (warning === 0 && caution === 0) {
    return null;
  }

  return (
    <span className="sb-counts" aria-hidden="true">
      {warning > 0 && <em className="sb-badge warning">{warning}</em>}
      {caution > 0 && <em className="sb-badge caution">{caution}</em>}
    </span>
  );
}

/**
 * The open alerts of the chosen environment, in numbers and in words.
 *
 * @param snapshot - The latest snapshot, or null before it arrives.
 * @returns The counts and a sentence for screen readers, or null without a snapshot.
 */
export function alertCounts(snapshot: HomeSnapshot | null): { warning: number; caution: number; words: string } | null {
  if (!snapshot) {
    return null;
  }

  const { warning, caution } = snapshot.counts;
  const parts = [
    warning > 0 ? `${warning} ${warning === 1 ? "warning" : "warnings"}` : "",
    caution > 0 ? `${caution} ${caution === 1 ? "caution" : "cautions"}` : "",
  ].filter(Boolean);

  return { warning, caution, words: parts.length === 0 ? "No open alerts" : parts.join(", ") };
}
