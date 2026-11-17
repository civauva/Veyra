import { useId, useState, type FormEvent } from "react";

import { api, ApiError } from "../api/client";

/**
 * The Data section's "Clear all telemetry" (VEY-59): deletes every log, metric, trace, SQL Server activity record,
 * and alert, so Veyra starts again from empty. Users, settings, agents, monitored servers, keys, and channels stay.
 * The Admin types the site's host name first, so a stray click cannot clear.
 *
 * @returns The panel.
 */
export function ClearTelemetry() {
  const site = window.location.hostname;
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputId = useId();
  const matches = typed.trim().toLowerCase() === site.toLowerCase();

  async function clear(event: FormEvent) {
    event.preventDefault();

    if (!matches || busy) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await api.clearTelemetry(typed.trim());
      setDone(true);
      setTyped("");
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : "Veyra could not clear the telemetry.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="settings-card clear-telemetry" aria-labelledby={`${inputId}-head`}>
      <header>
        <h3 id={`${inputId}-head`}>Clear all telemetry</h3>
      </header>
      <p>
        Deletes every log, metric, trace, SQL Server activity record, and alert, at once and for good. Users, settings,
        agents, monitored servers, ingestion keys, and notification channels stay, and monitoring carries on from empty.
        An alert whose condition still holds opens again within a minute.
      </p>
      <form onSubmit={(event) => void clear(event)}>
        <label htmlFor={inputId}>
          Type <b>{site}</b> to confirm
        </label>
        <div className="clear-telemetry-row">
          <input
            id={inputId}
            value={typed}
            onChange={(event) => {
              setTyped(event.target.value);
              setDone(false);
            }}
            autoComplete="off"
            spellCheck={false}
          />
          <button type="submit" className="button danger" disabled={!matches || busy}>
            {busy ? "Clearing…" : "Clear all telemetry"}
          </button>
        </div>
      </form>
      {done && (
        <p className="clear-telemetry-done" role="status">
          Cleared. Veyra keeps receiving telemetry from now on.
        </p>
      )}
      {error && (
        <p className="settings-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
