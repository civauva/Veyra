import { useId } from "react";

import { lookIds, lookNames } from "../api/looks";
import { HelpTip } from "../ui/HelpTip";
import { lookWords, useLookChoice } from "./useLookChoice";

/**
 * The look of the whole app for this person (VEY-71, ADR 0021), kept on their account so it follows them to every
 * device. Until they pick one they see the instance's default. When an Admin has set one look for everyone, the panel
 * says so and the choice is closed. The Account page and Settings → Look and feel both show it.
 */
export function LookPanel() {
  const look = useLookChoice();
  const titleId = useId();

  if (!look) {
    return null;
  }

  const { shown, fallback, mayChoose, own, busy, error } = look;
  const save = look.choose;

  return (
    <section className="account-panel account-full look-panel" aria-labelledby={titleId}>
      <div className="account-panel-head">
        <h2 id={titleId}>Look</h2>
        <HelpTip label="Look">
          How the whole app looks for you: the menu, the home, and every screen. The data, the alerts, and their words
          are the same in every look. Your choice is kept on your account, so it follows you to every device.
        </HelpTip>
      </div>
      {!mayChoose && <p className="look-note">An Admin has set one look for everyone: {lookNames[fallback]}.</p>}
      <div className="look-options" role="radiogroup" aria-labelledby={titleId}>
        {lookIds.map((id) => (
          <label key={id} className={id === shown ? "look-option on" : "look-option"}>
            <input
              type="radio"
              name={`${titleId}-look`}
              value={id}
              checked={id === shown}
              disabled={!mayChoose || busy}
              onChange={() => void save(id)}
            />
            <span className={`look-preview look-preview-${id}`} aria-hidden="true" />
            <span className="look-name">
              <b>{lookNames[id]}</b>
              {id === fallback && <small>Default</small>}
            </span>
            <span className="look-words">{lookWords[id]}</span>
          </label>
        ))}
      </div>
      {mayChoose && own && (
        <button type="button" className="button quiet small look-reset" disabled={busy} onClick={() => void save(null)}>
          Use the default, {lookNames[fallback]}
        </button>
      )}
      {error && (
        <p className="look-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
