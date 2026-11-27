import { useId } from "react";
import { NavLink } from "react-router";

import type { Session } from "../../api/types";
import { themeNames, type ThemeName } from "../../app/useTheme";
import type { Times } from "../../app/useTimes";
import { cityOf } from "../../time/zone";
import { usePopover } from "../../ui/usePopover";

/** Props for {@link ConsoleAccount}. */
interface ConsoleAccountProps {
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

const themeLabels: Record<ThemeName, string> = { system: "System", light: "Light", dark: "Dark" };

/**
 * The person at the right of the top bar: their initials, opening a menu with their name and role, their account
 * (where they also pick the look), the theme, the switch between their time zone and UTC (VEY-22), and signing out.
 */
export function ConsoleAccount({ session, theme, onTheme, times, onSignOut }: ConsoleAccountProps) {
  const { open, toggle, close, rootRef, buttonRef } = usePopover();
  const menuId = useId();
  const initials = session.displayName
    .split(/\s+/)
    .map((part) => part[0] ?? "")
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <div className="cn-account" ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        className="cn-avatar"
        aria-label={`Account: ${session.displayName}`}
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={toggle}
      >
        <span aria-hidden="true">{initials}</span>
      </button>
      {open && (
        <div id={menuId} className="cn-pop" role="dialog" aria-label="Account">
          <p className="cn-pop-who">
            <b>{session.displayName}</b>
            <small>
              {session.email} · {session.role}
            </small>
          </p>
          <NavLink to="/account" className="cn-pop-link" onClick={() => close()}>
            Your account and look
          </NavLink>
          <p className="cn-pop-cap" aria-hidden="true">
            Theme
          </p>
          <div className="cn-seg" role="group" aria-label="Theme">
            {themeNames.map((choice) => (
              <button key={choice} type="button" aria-pressed={theme === choice} onClick={() => onTheme(choice)}>
                {themeLabels[choice]}
              </button>
            ))}
          </div>
          {times.personal !== "UTC" && (
            <>
              <p className="cn-pop-cap" aria-hidden="true">
                Times
              </p>
              <div className="cn-seg" role="group" aria-label="Times">
                <button type="button" aria-pressed={!times.utc} onClick={() => times.setUtc(false)}>
                  {cityOf(times.personal)}
                </button>
                <button type="button" aria-pressed={times.utc} onClick={() => times.setUtc(true)}>
                  UTC
                </button>
              </div>
            </>
          )}
          <button type="button" className="cn-button cn-signout" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      )}
    </div>
  );
}
