import { useId, useState } from "react";

import type { Setting } from "../api/types";
import { formatClock } from "../logs/format";
import { shortDate } from "../time/zone";
import { describeValue, fromShown, toShown } from "./settingsFormat";

/** An edit of a setting: a value in configuration form, null to reset to the default, undefined for none. */
export type SettingEdit = string | null | undefined;

/** Props for {@link SettingField}. */
interface SettingFieldProps {
  /** The setting, with the value in effect. */
  setting: Setting;
  /** The person's unsaved edit, if any. */
  edit: SettingEdit;
  /** Why the last review refused the value, or null. */
  problem: string | null;
  /** Whether to mark the setting when it is an advanced one. */
  tagAdvanced?: boolean;
  /** Called with a new edit; undefined takes the edit back. */
  onEdit: (edit: SettingEdit) => void;
}

/**
 * The accessible name of a setting's control: its card and its label, such as "Error rate, Caution from", because
 * labels repeat from card to card.
 *
 * @param setting - The setting.
 * @returns The name.
 */
export function fieldName(setting: Setting): string {
  return `${setting.group}, ${setting.label}`;
}

/** Where a default comes from, as the note under a field says it. */
function sourceWords(setting: Setting): string {
  const { kind, name } = setting.defaultSource;

  if (kind === "environment" || kind === "file") {
    return `, from ${name}`;
  }

  return kind === "configuration" ? ", from the configuration" : "";
}

/**
 * One setting: its control, chosen by kind, and a note that says its default, who saved it, or what an edit changes.
 * A secret is never shown; it can only be replaced or removed.
 */
export function SettingField({ setting, edit, problem, tagAdvanced = false, onEdit }: SettingFieldProps) {
  const id = useId();
  const noteId = `${id}-note`;
  const name = fieldName(setting);
  const on = (edit !== undefined ? edit : setting.value) === "true";

  // A rule's own On switch sits bare in its card's header; any other switch is a field like the rest, with its name.
  if (setting.kind === "Switch" && setting.label === "On") {
    return (
      <div className="setting-field setting-switch">
        <button
          type="button"
          role="switch"
          className="switch"
          aria-checked={on}
          aria-label={name}
          onClick={() => onEdit(String(!on) === setting.value ? undefined : String(!on))}
        />
      </div>
    );
  }

  return (
    <div className="setting-field">
      <label htmlFor={id}>
        {setting.label}
        {tagAdvanced && setting.advanced && <span className="setting-tag">Advanced</span>}
      </label>
      <SettingControl setting={setting} edit={edit} problem={problem} id={id} noteId={noteId} onEdit={onEdit} />
      <SettingNote setting={setting} edit={edit} problem={problem} id={noteId} onEdit={onEdit} />
    </div>
  );
}

/** Props for {@link SettingControl}. */
export interface SettingControlProps {
  /** The setting, with the value in effect. */
  setting: Setting;
  /** The person's unsaved edit, if any. */
  edit: SettingEdit;
  /** Why the last review refused the value, or null. */
  problem: string | null;
  /** The control's element ID, which its label points to. */
  id: string;
  /** The ID of the note that describes it. */
  noteId: string;
  /** Called with a new edit; undefined takes the edit back. */
  onEdit: (edit: SettingEdit) => void;
}

/**
 * A setting's control, chosen by its kind: a switch, a choice, a secret that can only be replaced, or a box for a
 * number, a duration, or text with its unit. Every look draws it, each in its own row or field.
 */
export function SettingControl({ setting, edit, problem, id, noteId, onEdit }: SettingControlProps) {
  const name = fieldName(setting);
  const edited = edit !== undefined;
  const value = edited ? edit : setting.value;
  const [changingSecret, setChangingSecret] = useState(false);
  const on = value === "true";

  switch (setting.kind) {
    case "Switch":
      return (
        <button
          id={id}
          type="button"
          role="switch"
          className="switch"
          aria-checked={on}
          aria-label={name}
          aria-describedby={noteId}
          onClick={() => onEdit(String(!on) === setting.value ? undefined : String(!on))}
        />
      );
    case "Choice":
      return (
        <select
          id={id}
          aria-label={name}
          aria-describedby={noteId}
          value={value ?? ""}
          onChange={(event) => onEdit(event.target.value === setting.value ? undefined : event.target.value)}
        >
          {setting.choices.map((choice) => (
            <option key={choice} value={choice}>
              {choice}
            </option>
          ))}
        </select>
      );
    case "Secret":
      if (!changingSecret && !edited) {
        return (
          <span className="setting-secret">
            <span>{setting.isSet ? "Set · stored encrypted" : "Not set"}</span>
            <button
              type="button"
              className="button small quiet"
              aria-label={`Change ${setting.label}`}
              onClick={() => setChangingSecret(true)}
            >
              Change
            </button>
          </span>
        );
      }

      return (
        <input
          id={id}
          type="password"
          autoComplete="new-password"
          aria-label={name}
          aria-describedby={noteId}
          value={edit ?? ""}
          onChange={(event) => onEdit(event.target.value === "" ? undefined : event.target.value)}
        />
      );
    default:
      return (
        <span className={`setting-input${edited ? " edited" : ""}${problem ? " invalid" : ""}`}>
          <input
            id={id}
            type="text"
            inputMode={setting.kind === "Text" ? "text" : "decimal"}
            aria-label={name}
            aria-describedby={noteId}
            aria-invalid={problem ? true : undefined}
            value={toShown(setting, value ?? null)}
            placeholder={setting.optional ? "none" : undefined}
            onChange={(event) => {
              const next = fromShown(setting, event.target.value);

              onEdit(next === (setting.value ?? "") ? undefined : next);
            }}
          />
          {setting.unit && <em aria-hidden="true">{setting.unit}</em>}
        </span>
      );
  }
}

/** Props for {@link SettingNote}. */
export interface SettingNoteProps {
  /** The setting, with the value in effect. */
  setting: Setting;
  /** The person's unsaved edit, if any. */
  edit: SettingEdit;
  /** Why the last review refused the value, or null. */
  problem: string | null;
  /** The note's element ID, which its control points to. */
  id: string;
  /** Called with a new edit; undefined takes the edit back, null resets to the default. */
  onEdit: (edit: SettingEdit) => void;
}

/** The note under a setting: the problem, what an edit changes with Undo, who saved it with Reset, or its default. */
export function SettingNote({ setting, edit, problem, id, onEdit }: SettingNoteProps) {
  const edited = edit !== undefined;

  return (
    <span id={id} className={`setting-note${problem ? " problem" : edited ? " edited" : ""}`}>
      {problem ? (
        problem
      ) : edited ? (
        <>
          {edit === null
            ? `Resets to ${describeValue(setting, setting.default)}`
            : setting.kind === "Secret"
              ? "Replaces the stored value"
              : `Was ${describeValue(setting, setting.value)}`}{" "}
          ·{" "}
          <button type="button" className="link-button" onClick={() => onEdit(undefined)}>
            Undo
          </button>
        </>
      ) : setting.saved ? (
        <>
          Saved by {setting.savedBy ?? "someone"}
          {setting.savedAt ? ` · ${shortDate(setting.savedAt)} ${formatClock(setting.savedAt)}` : ""} ·{" "}
          <button
            type="button"
            className="link-button"
            aria-label={`Reset ${setting.label} to ${describeValue(setting, setting.default)}`}
            onClick={() => onEdit(null)}
          >
            Reset
          </button>
        </>
      ) : setting.kind === "Secret" ? (
        setting.help
      ) : (
        `Default ${describeValue(setting, setting.default)}${sourceWords(setting)}`
      )}
    </span>
  );
}
