import { useEffect, useId, useRef, useState } from "react";

import type { Setting, SettingsReview } from "../api/types";
import { closeOnEscape } from "../users/ResetPasswordDialog";
import { fieldName } from "./SettingField";
import { describeValue } from "./settingsFormat";

/** Props for {@link ReviewDialog}. */
interface ReviewDialogProps {
  /** What the changes will do, from the API. */
  review: SettingsReview;
  /** Every setting by key, to name the changes and show their values in words. */
  settings: Map<string, Setting>;
  /** The number of changes to save. */
  count: number;
  /** Where the changes come from, for an import; null for edits on the screen. */
  origin: string | null;
  /** Whether a save is under way. */
  busy: boolean;
  /** Why the last save failed, or null. */
  error: string | null;
  /** Saves, with whether the person confirmed risky changes. */
  onSave: (confirmed: boolean) => void;
  /** Goes back to the screen without saving. */
  onClose: () => void;
}

/** "1 change", "3 changes". */
export function changes(count: number): string {
  return count === 1 ? "1 change" : `${count} changes`;
}

/**
 * The last look before saving: each change from and to, what it will do, and, when it deletes data or may lock
 * people out, an explicit confirmation (ADR 0017).
 */
export function ReviewDialog({ review, settings, count, origin, busy, error, onSave, onClose }: ReviewDialogProps) {
  const titleId = useId();
  const [confirmed, setConfirmed] = useState(false);
  const first = useRef<HTMLButtonElement>(null);

  useEffect(() => closeOnEscape(onClose), [onClose]);
  useEffect(() => first.current?.focus(), []);

  return (
    <>
      <div className="settings-shade" aria-hidden="true" onClick={onClose} />
      <div className="settings-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 id={titleId}>Review {changes(count)}</h2>
        {origin && <p className="settings-dialog-origin">From {origin}</p>}
        <ul className="review-changes">
          {review.changes.map((change) => {
            const setting = settings.get(change.key);
            const notes = review.notes.filter((note) => note.key === change.key);
            const risky = notes.some((note) => note.risky);

            return (
              <li key={change.key} className={risky ? "risky" : undefined}>
                <b>{setting ? fieldName(setting) : change.key}</b>
                <span className="review-values">
                  {setting?.kind === "Secret"
                    ? "replaced"
                    : setting
                      ? `${describeValue(setting, change.from)} → ${describeValue(setting, change.to)}`
                      : `${change.from ?? "none"} → ${change.to ?? "default"}`}
                </span>
                {notes.map((note) => (
                  <p key={note.text}>{note.text}</p>
                ))}
              </li>
            );
          })}
        </ul>
        {review.confirmationRequired && (
          <label className="review-confirm">
            <input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />I
            understand what these changes delete or whom they may lock out.
          </label>
        )}
        {error && (
          <p className="settings-error" role="alert">
            {error}
          </p>
        )}
        <div className="settings-dialog-actions">
          <button ref={first} type="button" className="button quiet" onClick={onClose}>
            Back
          </button>
          <button
            type="button"
            className="button primary"
            disabled={busy || (review.confirmationRequired && !confirmed)}
            onClick={() => onSave(confirmed)}
          >
            Save {changes(count)}
          </button>
        </div>
      </div>
    </>
  );
}
