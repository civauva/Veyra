import { useCallback, useEffect, useId, useState } from "react";

import { api, ApiError } from "../api/client";
import type { Setting, SettingHistoryItem, SettingsPage, SettingsReview } from "../api/types";
import { formatClock } from "../logs/format";
import { shortDate } from "../time/zone";
import { closeOnEscape } from "../users/ResetPasswordDialog";
import { sections, type SectionId } from "./settingSections";
import { describeValue } from "./settingsFormat";

/** Props for {@link SettingsHistoryPanel}. */
interface SettingsHistoryPanelProps {
  /** Every setting by key, to show values in words. */
  settings: Map<string, Setting>;
  /** The settings version, which a revert must name. */
  version: number;
  /** Called with the settings after a revert. */
  onReverted: (page: SettingsPage) => void;
  /** Closes the panel. */
  onClose: () => void;
}

/** What a change did, in words: "3 s → 2 s", "Changed, hidden", "14 days → default". */
function whatChanged(change: SettingHistoryItem, setting: Setting | undefined): string {
  if (change.secret) {
    return "Changed, hidden";
  }

  const say = (value: string | null) => (setting ? describeValue(setting, value) : (value ?? "default"));

  // A side without a saved value was the default; name it when the setting is known.
  const side = (value: string | null) =>
    value !== null ? say(value) : setting ? `${describeValue(setting, setting.default)} (default)` : "default";

  return `${side(change.oldValue)} → ${side(change.newValue)}`;
}

/** The initials of a name, for the avatar of a change. */
function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
}

/**
 * Every change to the settings, newest first, filtered by section, with Revert. A revert that deletes data or may
 * lock people out asks first (ADR 0017).
 */
export function SettingsHistoryPanel({ settings, version, onReverted, onClose }: SettingsHistoryPanelProps) {
  const titleId = useId();
  const [section, setSection] = useState<SectionId | null>(null);
  const [changes, setChanges] = useState<SettingHistoryItem[]>([]);
  const [next, setNext] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [asking, setAsking] = useState<{ id: number; review: SettingsReview } | null>(null);

  useEffect(() => closeOnEscape(onClose), [onClose]);

  const load = useCallback(
    (before: number | null, signal?: AbortSignal) => {
      const params = new URLSearchParams({ limit: "50" });

      if (section) {
        params.set("section", section);
      }

      if (before !== null) {
        params.set("before", String(before));
      }

      api
        .settingsHistory(params, signal)
        .then((page) => {
          setChanges((current) => (before === null ? page.changes : [...current, ...page.changes]));
          setNext(page.next);
          setError(null);
        })
        .catch((failure: unknown) => {
          if (!signal?.aborted) {
            setError(failure instanceof Error ? failure.message : String(failure));
          }
        });
    },
    [section],
  );

  useEffect(() => {
    const controller = new AbortController();
    load(null, controller.signal);

    return () => controller.abort();
  }, [load]);

  async function revert(id: number, confirmed: boolean) {
    try {
      const page = await api.revertSetting(id, version, confirmed);
      setAsking(null);
      onReverted(page);
      load(null);
    } catch (failure) {
      const review = failure instanceof ApiError ? (failure.body as SettingsReview | undefined) : undefined;

      if (review?.confirmationRequired && !confirmed) {
        setAsking({ id, review });
      } else {
        setError(failure instanceof Error ? failure.message : String(failure));
      }
    }
  }

  return (
    <aside className="settings-history" aria-labelledby={titleId}>
      <header>
        <h2 id={titleId}>Settings history</h2>
        <button type="button" className="button small quiet" aria-label="Close the history" onClick={onClose}>
          ✕
        </button>
      </header>
      <div className="settings-history-filter" role="group" aria-label="Show changes of">
        {[{ id: null, title: "All sections" }, ...sections.filter((entry) => entry.id !== "System")].map((entry) => (
          <button
            key={entry.id ?? "all"}
            type="button"
            className={`chip-button${section === entry.id ? " on" : ""}`}
            aria-pressed={section === entry.id}
            onClick={() => setSection(entry.id as SectionId | null)}
          >
            {entry.title}
          </button>
        ))}
      </div>
      {error && (
        <p className="settings-error" role="alert">
          {error}
        </p>
      )}
      {changes.length === 0 && !error && <p className="settings-history-empty">No changes yet.</p>}
      <ol className="settings-history-list">
        {changes.map((change) => {
          const setting = settings.get(change.key);

          return (
            <li key={change.id}>
              <span className="settings-avatar" aria-hidden="true">
                {initials(change.changedByName)}
              </span>
              <div>
                <b>
                  {change.group ? `${change.group} · ` : ""}
                  {change.label}
                </b>
                <span className="settings-history-when">
                  {change.changedByName} · {shortDate(change.changedAt)} {formatClock(change.changedAt)}
                  {change.kind === "Reverted" ? " · reverted" : change.kind === "Reset" ? " · reset" : ""}
                </span>
                <span className="settings-history-values">{whatChanged(change, setting)}</span>
                {change.origin && <span className="settings-history-when">From {change.origin}</span>}
                {asking?.id === change.id && (
                  <div className="settings-history-ask" role="alert">
                    {asking.review.notes.map((note) => (
                      <p key={note.text}>{note.text}</p>
                    ))}
                    <button type="button" className="button small danger" onClick={() => void revert(change.id, true)}>
                      Revert anyway
                    </button>
                  </div>
                )}
                {change.canRevert && asking?.id !== change.id && (
                  <button
                    type="button"
                    className="link-button"
                    aria-label={`Revert ${change.label}`}
                    onClick={() => void revert(change.id, false)}
                  >
                    Revert
                  </button>
                )}
              </div>
            </li>
          );
        })}
      </ol>
      {next !== null && (
        <button type="button" className="button small" onClick={() => load(next)}>
          Show older changes
        </button>
      )}
    </aside>
  );
}
