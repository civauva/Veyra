import { useState, type FormEvent } from "react";

import { api } from "../api/client";
import type { AddedAgent } from "../api/types";

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** Props for {@link AddAgentCard}. */
export interface AddAgentCardProps {
  /** Called with the new agent and its one-time code. */
  onAdded: (added: AddedAgent) => void;
}

/**
 * The card that adds an agent for a network Veyra cannot reach: what it is for, then a name.
 *
 * @param props - What to do with the new agent.
 * @returns The card.
 */
export function AddAgentCard({ onAdded }: AddAgentCardProps) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function add(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      onAdded(await api.addAgent(name.trim()));
      setName("");
      setOpen(false);
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="monitor-card monitor-later">
      <b>An agent for another network</b>
      <p>
        For SQL Servers the built-in agent cannot reach, such as those behind a company firewall while Veyra runs
        elsewhere. You install it on any machine in that network; it connects out to Veyra, and nothing connects in.
      </p>
      {open ? (
        <form className="monitor-add-agent" onSubmit={(event) => void add(event)}>
          <label className="monitor-field">
            <span>Name</span>
            <input
              value={name}
              required
              maxLength={60}
              placeholder="Company network"
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </label>
          <button type="submit" className="button primary" disabled={busy}>
            Add agent
          </button>
          <button type="button" className="button quiet" onClick={() => setOpen(false)}>
            Cancel
          </button>
        </form>
      ) : (
        <button type="button" className="button" onClick={() => setOpen(true)}>
          Add an agent
        </button>
      )}
      {error && (
        <p className="settings-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
