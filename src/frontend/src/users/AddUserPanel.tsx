import { useEffect, useId, useRef, useState, type FormEvent } from "react";

import { api } from "../api/client";
import type { Role, UserSummary } from "../api/types";
import { MadePassword } from "./MadePassword";
import { newPassword } from "./passwords";
import { closeOnEscape } from "./ResetPasswordDialog";
import { roleReach, roles } from "./userFormat";

/** Props for {@link AddUserPanel}. */
interface AddUserPanelProps {
  /** Called with the new person. */
  onAdded(person: UserSummary): void;
  /** Closes the panel. */
  onClose(): void;
}

/**
 * Adds a person: email, name, team, how far in they reach, and a first password Veyra makes up. Veyra sends no
 * email, so the Admin hands the password over.
 */
export function AddUserPanel({ onAdded, onClose }: AddUserPanelProps) {
  const titleId = useId();
  const roleName = useId();
  const first = useRef<HTMLInputElement>(null);
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [team, setTeam] = useState("");
  const [role, setRole] = useState<Role>("Viewer");
  const [password, setPassword] = useState(newPassword);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    first.current?.focus();
  }, []);

  useEffect(() => closeOnEscape(onClose), [onClose]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      onAdded(
        await api.createUser({
          email: email.trim(),
          displayName: name.trim(),
          role,
          password,
          team: team.trim() || null,
        }),
      );
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  const firstName = name.trim().split(/\s+/)[0];

  return (
    <>
      <div className="people-shade" aria-hidden="true" onClick={onClose} />
      <aside className="people-drawer" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <header>
          <h2 id={titleId}>Add a user</h2>
          <p>
            They sign in with their email and the first password below. Veyra sends no email: hand the password over in
            person or through a channel you trust.
          </p>
        </header>
        <form className="people-form" onSubmit={(event) => void submit(event)}>
          <label>
            Email
            <input ref={first} type="email" required value={email} onChange={(event) => setEmail(event.target.value)} />
          </label>
          <div className="people-field">
            <label htmlFor={`${titleId}-name`}>Name</label>
            <input
              id={`${titleId}-name`}
              aria-describedby={`${titleId}-name-help`}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
            <small id={`${titleId}-name-help`}>
              Shown in the top bar and on the badges. Left empty, the email is used.
            </small>
          </div>
          <label>
            Team
            <input
              value={team}
              maxLength={100}
              placeholder="For example Payments"
              onChange={(event) => setTeam(event.target.value)}
            />
          </label>
          <fieldset className="rungs">
            <legend>How far in</legend>
            {roles.map((option) => (
              <label key={option} className={`r-${option}${role === option ? " on" : ""}`}>
                <input type="radio" name={roleName} checked={role === option} onChange={() => setRole(option)} />
                <MiniOrbit role={option} />
                <b>{option}</b>
                <span>{roleReach[option]}</span>
              </label>
            ))}
          </fieldset>
          <div className="people-field">
            <span>First password</span>
            <MadePassword
              value={password}
              onOther={() => setPassword(newPassword())}
              label="First password"
              note={`Four random words. Shown only now; ${firstName || "they"} change${firstName ? "s" : ""} it on the Account page.`}
            />
          </div>
          {error && (
            <p className="people-error" role="alert">
              {error}
            </p>
          )}
          <div className="people-foot sticky">
            <button type="button" className="button quiet" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="button primary" disabled={busy}>
              {firstName ? `Add ${firstName}` : "Add user"}
            </button>
          </div>
        </form>
      </aside>
    </>
  );
}

/** Props for {@link MiniOrbit}. */
interface MiniOrbitProps {
  /** The role whose ring lights up. */
  role: Role;
}

/** The nested rings of the roles, small, with the new person's place lit: roles build on each other. */
function MiniOrbit({ role }: MiniOrbitProps) {
  const dot = role === "Admin" ? [32, 32] : role === "Editor" ? [51, 21] : [58, 47];

  return (
    <svg width="56" height="56" viewBox="0 0 64 64" aria-hidden="true" className="mini-orbit">
      <circle cx="32" cy="32" r="29" className={role === "Viewer" ? "lit" : undefined} />
      <circle cx="32" cy="32" r="20" className={role === "Editor" ? "lit" : undefined} />
      <circle cx="32" cy="32" r="10" className={role === "Admin" ? "lit core" : "core"} />
      <circle cx={dot[0]} cy={dot[1]} r="5.5" className="me" />
    </svg>
  );
}
