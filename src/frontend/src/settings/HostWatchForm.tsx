import { useId, useState } from "react";

import { api } from "../api/client";
import type { Agent } from "../api/types";

/** Whether an agent says it runs Windows, where the Event Log and IIS are read; Docker is read everywhere else. */
function runsWindows(agent: Agent): boolean {
  return /windows/i.test(agent.operatingSystem ?? "");
}

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/**
 * The props of {@link HostWatchForm}.
 */
export interface HostWatchFormProps {
  /** The installed agent whose host is set. */
  agent: Agent;
  /** Called after the setting was saved, so the list can ask for the agent again. */
  onSaved: () => void;
}

/**
 * "Watch this host" on an installed agent's card (ADR 0018, VEY-35): the agent reads the CPU, memory, disks, and
 * network of the machine it runs on, on Windows optionally the Event Log's errors and IIS, and on Linux optionally
 * its Docker containers (VEY-32).
 *
 * @param props - See {@link HostWatchFormProps}.
 * @returns The switches, the environment, and a Save button.
 */
export function HostWatchForm({ agent, onSaved }: HostWatchFormProps) {
  const id = useId();
  const windows = runsWindows(agent);
  const [host, setHost] = useState(agent.host);
  const [eventLog, setEventLog] = useState(agent.eventLog);
  const [iis, setIis] = useState(agent.iis);
  const [docker, setDocker] = useState(agent.docker);
  const [environment, setEnvironment] = useState(agent.hostEnvironment);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const changed =
    host !== agent.host ||
    (host &&
      (eventLog !== agent.eventLog ||
        iis !== agent.iis ||
        docker !== agent.docker ||
        environment.trim() !== agent.hostEnvironment));

  async function save() {
    setSaving(true);
    setError(null);

    try {
      await api.setAgentHost(agent.id, {
        host,
        eventLog: host && windows && eventLog,
        iis: host && windows && iis,
        docker: host && !windows && docker,
        environment: environment.trim(),
      });
      onSaved();
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setSaving(false);
    }
  }

  function row(label: string, on: boolean, set: (on: boolean) => void, hint?: string) {
    return (
      <div className="monitor-switch">
        <button
          type="button"
          role="switch"
          className="switch"
          aria-checked={on}
          aria-label={label}
          onClick={() => set(!on)}
        />
        <span aria-hidden="true">
          {label}
          {hint && <small>{hint}</small>}
        </span>
      </div>
    );
  }

  return (
    <section className="monitor-host" aria-label={`Host of ${agent.name}`}>
      {row("Watch this host", host, setHost, "CPU, memory, disks, and network of the machine it runs on")}
      {host && (
        <>
          <label className="monitor-field" htmlFor={`${id}-environment`}>
            <span>Environment</span>
            <input
              id={`${id}-environment`}
              value={environment}
              required
              maxLength={64}
              onChange={(event) => setEnvironment(event.target.value)}
            />
          </label>
          {windows && row("Event Log errors", eventLog, setEventLog, "The System and Application logs")}
          {windows && row("IIS sites", iis, setIis, "IIS metrics and every site's requests")}
          {!windows && row("Docker containers", docker, setDocker, "Each container's CPU, memory, restarts, and logs")}
        </>
      )}
      {error && (
        <p className="monitor-why bad" role="alert">
          {error}
        </p>
      )}
      {changed && (
        <button type="button" className="button small" disabled={saving} onClick={() => void save()}>
          {saving ? "Saving…" : "Save"}
        </button>
      )}
    </section>
  );
}
