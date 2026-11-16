import { useCallback, useEffect, useId, useState } from "react";
import { Link } from "react-router";

import { databasePath } from "../alerts/ruleWords";
import { api } from "../api/client";
import type { Agent, SqlServer } from "../api/types";
import { DatabaseIcon } from "../metrics/MetricCards";
import { AgentStrip } from "./AgentStrip";
import { readerOf, serverExplanation, serverStateWords } from "./monitoringFormat";
import { SqlServerPanel } from "./SqlServerPanel";
import { StateChip } from "./StateChip";
import "./monitoring.css";

/** How often the list asks again, so a state changes on screen without a reload. */
const refreshEvery = 10_000;

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/**
 * Settings → Database monitoring (ADR 0019): the SQL Servers Veyra's agents read, each with its state in words, and
 * the panel to add or change one. Nothing else needs editing: Veyra sends each agent its settings.
 *
 * @returns The page's content, inside the Settings screen.
 */
export function DatabaseMonitoring() {
  const [servers, setServers] = useState<SqlServer[] | null>(null);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<{ server: SqlServer | null } | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);
  const [now, setNow] = useState(() => new Date());
  const addHelpId = useId();

  const load = useCallback(
    () =>
      Promise.all([api.sqlServers(), api.agents()])
        .then(([listed, known]) => {
          setServers(listed);
          setAgents(known);
          setNow(new Date());
          setError(null);
        })
        .catch((failure: unknown) => setError(messageOf(failure))),
    [],
  );

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), refreshEvery);

    return () => window.clearInterval(timer);
  }, [load]);

  async function remove(server: SqlServer) {
    try {
      await api.deleteSqlServer(server.id);
      setRemoving(null);
      await load();
    } catch (failure) {
      setError(messageOf(failure));
    }
  }

  // Both lists load together, so a loaded server list means the agents are known too.
  const loaded = servers !== null;
  const noAgent = loaded && agents.length === 0;

  return (
    <div className="monitor">
      {loaded && <AgentStrip agents={agents} now={now} />}

      <div className="monitor-bar">
        <div className="monitor-kinds">
          <span className="monitor-kind on">
            <DatabaseIcon size={16} />
            SQL Server <small>{servers?.length ?? "…"}</small>
          </span>
          <span className="monitor-kind soon">
            <DatabaseIcon size={16} />
            PostgreSQL <small>soon</small>
          </span>
        </div>
        <button
          type="button"
          className="button primary"
          disabled={noAgent}
          aria-describedby={noAgent ? addHelpId : undefined}
          onClick={() => setEditing({ server: null })}
        >
          + Add SQL Server
        </button>
        {noAgent && (
          <span id={addHelpId} className="visually-hidden">
            Needs an agent first
          </span>
        )}
      </div>

      {error && (
        <p className="settings-error" role="alert">
          {error}
        </p>
      )}

      {servers?.length === 0 && !noAgent && (
        <div className="monitor-card monitor-empty">
          <b>No SQL Server yet</b>
          <p>
            Add one: Veyra writes the login script for it, sends the settings to the agent that reads it, and shows here
            what the agent found. No file to edit, no variable to set.
          </p>
        </div>
      )}

      {servers?.map((server) => {
        const words = serverStateWords(server.state);

        return (
          <article
            key={server.id}
            className={`monitor-card monitor-server${words.tone === "bad" ? " bad" : ""}`}
            aria-label={server.name}
          >
            <span className="monitor-icon">
              <DatabaseIcon />
            </span>
            <div className="monitor-main">
              <b>{server.name}</b>
              <span className="monitor-meta">
                {server.address}:{server.port} · {server.environment} · read by {readerOf(server, agents)}
              </span>
              <span className={`monitor-why ${words.tone}`}>
                {serverExplanation(server, now, readerOf(server, agents))}
              </span>
            </div>
            <div className="monitor-actions">
              <StateChip label={words.label} tone={words.tone} />
              {server.state === "Receiving" && (
                <Link className="button small" to={databasePath(`microsoft.sql_server ${server.name}`)}>
                  Open
                </Link>
              )}
              <button
                type="button"
                className={`button small${words.tone === "bad" ? "" : " quiet"}`}
                onClick={() => setEditing({ server })}
              >
                {words.tone === "bad" ? "Fix" : "Edit"}
              </button>
              {removing === server.id ? (
                <>
                  <button type="button" className="button small danger" onClick={() => void remove(server)}>
                    Remove {server.name}
                  </button>
                  <button type="button" className="button small quiet" onClick={() => setRemoving(null)}>
                    Keep
                  </button>
                </>
              ) : (
                <button type="button" className="button small quiet" onClick={() => setRemoving(server.id)}>
                  Remove
                </button>
              )}
            </div>
          </article>
        );
      })}

      <div className="monitor-card monitor-rules">
        <div>
          <b>Alerts for these servers</b>
          <p>Built-in rules judge every server above. Their levels are under Alert rules.</p>
        </div>
        <div className="monitor-chips">
          <span>Blocking</span>
          <span>Deadlocks</span>
          <span>Long query</span>
          <span>Backup age</span>
          <span>SQL Agent jobs</span>
        </div>
        <Link className="button small" to="/settings?section=Rules">
          Alert rules ›
        </Link>
      </div>

      {editing && (
        <SqlServerPanel
          server={editing.server}
          agents={agents}
          onClose={() => {
            setEditing(null);
            void load();
          }}
          onSaved={() => void load()}
        />
      )}
    </div>
  );
}
