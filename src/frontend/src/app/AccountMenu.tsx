import { useId } from "react";
import { NavLink } from "react-router";

import type { Session } from "../api/types";
import { NavIcon } from "./NavIcon";
import { cityOf } from "../time/zone";
import { ThemeChoice, type ThemeName } from "./ThemeChoice";
import type { Times } from "./useTimes";
import { useBlendedNavigation } from "./useBlendedNavigation";
import { usePopover } from "./usePopover";

/** Props for {@link AccountMenu}. */
interface AccountMenuProps {
  /** The signed-in person. */
  session: Session;
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
 * The person at the foot of the sidebar (VEY-26): their initials, name, and role, opening a menu with their account,
 * the theme, the switch between their time zone and UTC (VEY-22), and signing out.
 */
export function AccountMenu({ session, theme, onTheme, times, onSignOut }: AccountMenuProps) {
  const { open, toggle, close, rootRef, buttonRef } = usePopover();
  const blend = useBlendedNavigation();
  const menuId = useId();

  const initials = session.displayName
    .split(/\s+/)
    .map((part) => part[0] ?? "")
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <div className="sb-account" ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        className="sb-me"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={toggle}
      >
        <span className="sb-avatar" aria-hidden="true">
          {initials}
        </span>
        <span className="sb-me-text sb-label">
          <b>{session.displayName}</b> <small>{session.role}</small>
        </span>
        <NavIcon name="more" size={18} className="sb-me-more" />
      </button>
      {open && (
        <div id={menuId} className="sb-pop account-menu" role="dialog" aria-label="Account">
          <NavLink
            to="/account"
            className="account-link"
            onClick={(event) => {
              blend(event, "/account");
              close();
            }}
          >
            Your account
          </NavLink>
          <p className="account-menu-cap" aria-hidden="true">
            Theme
          </p>
          <ThemeChoice value={theme} onChange={onTheme} />
          {times.personal !== "UTC" && (
            <p className="account-menu-cap" aria-hidden="true">
              Times
            </p>
          )}
          {times.personal !== "UTC" && (
            <div className="theme-choice" role="group" aria-label="Times">
              <button type="button" aria-pressed={!times.utc} onClick={() => times.setUtc(false)}>
                {cityOf(times.personal)}
              </button>
              <button type="button" aria-pressed={times.utc} onClick={() => times.setUtc(true)}>
                UTC
              </button>
            </div>
          )}
          <button type="button" className="button account-signout" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      )}
    </div>
  );
}
