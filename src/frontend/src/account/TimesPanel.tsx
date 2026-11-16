import { type FormEvent, useId, useMemo, useState } from "react";

import { useSession } from "../session/useSession";
import { browserZone, isKnownZone, offsetLabel } from "../time/zone";

/**
 * The Times section of the Account page (VEY-22): the time zone every screen shows the person's times in, kept on
 * the account so it follows them to every device. Until they pick one the browser's zone is used. The list is the
 * browser's own list of IANA zones, searchable by typing, with each zone's offset from UTC today.
 */
export function TimesPanel() {
  const { state, setTimeZone } = useSession();
  const chosen = state.status === "signedIn" ? state.session.timeZone : null;
  const browser = browserZone();
  const [value, setValue] = useState(chosen ?? browser);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const listId = useId();
  const zones = useMemo(() => Intl.supportedValuesOf("timeZone"), []);

  async function save(zone: string | null) {
    setBusy(true);
    setError(null);
    setSaved(null);

    try {
      await setTimeZone(zone);
      setSaved(zone);

      if (zone === null) {
        setValue(browser);
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const name = value.trim();

    if (!isKnownZone(name)) {
      setSaved(null);
      setError("Pick a zone from the list, such as Europe/Berlin.");

      return;
    }

    // The browser accepts any spelling of a zone; the account keeps its standard name.
    void save(new Intl.DateTimeFormat("en-US", { timeZone: name }).resolvedOptions().timeZone);
  }

  return (
    <section className="account-panel" aria-labelledby="account-times">
      <h2 id="account-times">Times</h2>
      <p className="account-info">
        Every screen shows times in your zone, on every device you sign in from. Days, such as Today on the Alerts
        screen and the 30-day log, start at your midnight. The account menu switches to UTC in one click.
      </p>
      <form className="account-form" onSubmit={submit}>
        <label>
          Time zone
          <input
            list={listId}
            value={value}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setValue(event.target.value)}
          />
        </label>
        <datalist id={listId}>
          {zones.map((zone) => (
            <option key={zone} value={zone}>
              {offsetLabel(zone)}
            </option>
          ))}
        </datalist>
        <p className="account-small">
          {chosen
            ? `Times show in ${chosen}, ${offsetLabel(chosen)} today.`
            : `Until you pick one, Veyra uses your browser's zone, ${browser}.`}
        </p>
        {error && (
          <p className="people-error" role="alert">
            {error}
          </p>
        )}
        {saved !== null && (
          <p className="account-done" role="status">
            Saved. Times show in {saved}.
          </p>
        )}
        <div className="account-right">
          {chosen !== null && (
            <button type="button" className="button quiet" disabled={busy} onClick={() => void save(null)}>
              Use my browser&apos;s zone
            </button>
          )}
          <button type="submit" className="button primary" disabled={busy || value.trim() === ""}>
            Save
          </button>
        </div>
      </form>
    </section>
  );
}
