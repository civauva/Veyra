import type { ReactNode, Ref } from "react";
import { Link, NavLink, useSearchParams } from "react-router";

import type { Session } from "../../api/types";
import { alertCounts, type AlertCounts } from "../../app/alertCounts";
import { navFor, type NavItem } from "../../app/look";
import type { ShellStatus } from "../../app/shellContext";
import { carryTime } from "../../time/carryTime";
import type { ThemeName } from "../../app/useTheme";
import type { Times } from "../../app/useTimes";
import { Logo } from "../../ui/Logo";
import { NavIcon, type NavIconName } from "../../ui/NavIcon";
import { AccountMenu } from "./AccountMenu";
import { forecastNav } from "./nav";
import { EnvironmentSwitcher } from "./EnvironmentSwitcher";
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
  /** The version this page runs, at the foot; null before it is known (VEY-82). */
  version: string | null;
  /** Whether a newer version runs, which marks the version. */
  newer: boolean;
  /** Opens the release notes. */
  onNotes(): void;
}

/**
 * The left sidebar (VEY-26), the same on every screen: the logo, which leads home; the environment every screen
 * shows; the screens, each marked in its own colour when it is open, with the open alerts counted beside Alerts;
 * the settings for administrators (ADR 0008); the person, with their account, the theme, and signing out; and the
 * version, which opens the release notes (VEY-82).
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
  version,
  newer,
  onNotes,
}: SidebarProps) {
  const counts = alertCounts(status.snapshot);
  const groups = navFor({ nav: forecastNav }, session.role);

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
      {groups
        .filter((group) => group.title === null)
        .map((group, index) => (
          <ul key={index} className="sb-list">
            {group.items.map((item) => (
              <Entry key={item.route.id} item={item} counts={counts} />
            ))}
          </ul>
        ))}
      <span className="sb-grow" />
      {groups
        .filter((group) => group.title !== null)
        .map((group) => {
          const titleId = `sb-${(group.title ?? "").toLowerCase()}-title`;

          return (
            <section key={group.title} className="sb-admin" aria-labelledby={titleId}>
              <p id={titleId} className="sb-cap">
                {group.title}
              </p>
              <ul className="sb-list">
                {group.items.map((item) => (
                  <Entry key={item.route.id} item={item} counts={counts} />
                ))}
              </ul>
            </section>
          );
        })}
      <AccountMenu session={session} theme={theme} onTheme={onTheme} times={times} onSignOut={onSignOut} />
      {version && (
        <button
          type="button"
          className={newer ? "sb-version newer" : "sb-version"}
          aria-label={`Veyra ${version}: what's new${newer ? ", a newer version is ready" : ""}`}
          onClick={onNotes}
        >
          {form === "mini" ? version : `Veyra ${version}`}
        </button>
      )}
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

/**
 * One entry of the menu. Alerts carries the open alerts, in badges and in words for screen readers.
 *
 * @param props - The entry and the open alerts.
 */
function Entry({ item, counts }: { item: NavItem; counts: AlertCounts | null }) {
  const { route, label } = item;

  if (route.id === "alerts") {
    return (
      <Item to={route.path} label={label} icon={route.icon} description={counts?.words}>
        {counts && <AlertBadges warning={counts.warning} caution={counts.caution} />}
      </Item>
    );
  }

  return <Item to={route.path} end={route.path === "/"} label={label} icon={route.icon} />;
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
  // Moving to another screen keeps the range on screen (ADR 0022).
  const [params] = useSearchParams();
  const target = carryTime(to, params);

  return (
    <li>
      <NavLink
        to={target}
        end={end}
        className={`sb-item sb-${icon}`}
        aria-describedby={describedBy}
        onClick={(event) => blend(event, target)}
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
