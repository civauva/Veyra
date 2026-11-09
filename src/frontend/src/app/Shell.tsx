import { NavLink, Outlet } from "react-router";

import type { Session } from "../api/types";
import { useSession } from "../session/useSession";
import { Logo } from "./Logo";
import { ThemeToggle } from "./ThemeToggle";
import "./shell.css";

/** Props for {@link Shell}. */
interface ShellProps {
  /** The signed-in user. */
  session: Session;
}

/**
 * The frame around every screen after sign-in: the top bar with navigation, theme, and the user's menu.
 * Navigation shows only what the user's role allows (ADR 0008).
 */
export function Shell({ session }: ShellProps) {
  const { signOut } = useSession();
  const initials = session.displayName
    .split(/\s+/)
    .map((part) => part[0] ?? "")
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <div className="shell">
      <header className="shell-bar">
        <span className="shell-brand">
          <Logo />
          <span className="shell-name">veyra</span>
        </span>
        <nav className="shell-nav" aria-label="Main">
          <NavLink to="/logs">Logs</NavLink>
          {session.role === "Admin" && <NavLink to="/keys">Keys</NavLink>}
        </nav>
        <span className="shell-spacer" />
        <ThemeToggle />
        <span className="shell-user" title={`${session.displayName} · ${session.role}`} aria-hidden="true">
          {initials}
        </span>
        <button type="button" className="button shell-signout" onClick={() => void signOut()}>
          Sign out
        </button>
      </header>
      <Outlet />
    </div>
  );
}
