import { useEffect, useId, useState } from "react";

import { api } from "../api/client";
import type { UserSummary } from "../api/types";
import { Avatar } from "./Avatar";
import { MadePassword } from "./MadePassword";
import { newPassword } from "./passwords";

/** Props for {@link ResetPasswordDialog}. */
interface ResetPasswordDialogProps {
  /** The person whose password is reset. */
  person: UserSummary;
  /** Whether a lock is on now, which the reset lifts. */
  locked: boolean;
  /** Called after the reset. */
  onDone(): void;
  /** Called when the Admin backs out. */
  onClose(): void;
}

/**
 * Sets a new password for someone, made up as four words. Says plainly what happens: the lock lifts, every session
 * ends, and the old password stops working (ADR 0011).
 */
export function ResetPasswordDialog({ person, locked, onDone, onClose }: ResetPasswordDialogProps) {
  const titleId = useId();
  const first = person.displayName.split(/\s+/)[0] ?? person.displayName;
  const [password, setPassword] = useState(newPassword);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => closeOnEscape(onClose), [onClose]);

  async function reset() {
    setBusy(true);
    setError(null);

    try {
      await api.resetPassword(person.id, password);
      onDone();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  return (
    <>
      <div className="people-shade" aria-hidden="true" onClick={onClose} />
      <div className="people-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <div className="dialog-who">
          <Avatar id={person.id} name={person.displayName} state={locked ? "locked" : "active"} />
          <div>
            <h2 id={titleId}>Reset {first}&apos;s password</h2>
            <small>
              {person.email} · {person.role}
            </small>
          </div>
        </div>
        <ol className="dialog-steps">
          {locked && <li>The lock lifts, so {first} can sign in again at once.</li>}
          <li>{first} is signed out on every browser and device.</li>
          <li>The old password stops working; this one replaces it.</li>
        </ol>
        <MadePassword
          value={password}
          onOther={() => setPassword(newPassword())}
          label="New password"
          note={`Shown only now. Ask ${first} to change it on the Account page.`}
        />
        {error && (
          <p className="people-error" role="alert">
            {error}
          </p>
        )}
        <div className="people-foot">
          <button type="button" className="button quiet" onClick={onClose}>
            Cancel
          </button>
          <button type="button" className="button primary" disabled={busy} onClick={() => void reset()}>
            Reset password
          </button>
        </div>
      </div>
    </>
  );
}

/**
 * Closes a dialog with Escape.
 *
 * @param onClose - What closes it.
 * @returns The clean-up for an effect.
 */
export function closeOnEscape(onClose: () => void): () => void {
  function onKey(event: KeyboardEvent) {
    if (event.key === "Escape") {
      onClose();
    }
  }

  window.addEventListener("keydown", onKey);

  return () => window.removeEventListener("keydown", onKey);
}
