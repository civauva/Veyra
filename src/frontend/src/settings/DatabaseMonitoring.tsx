import { useCallback, useEffect, useId, useState } from "react";
import { Link } from "react-router";

import { databasePath } from "../alerts/ruleWords";
import { api } from "../api/client";
import type { Agent, MonitoredServer, PostgresServer, SqlServer } from "../api/types";
import { integrationPath } from "../integrations/integrationWords";
import { HelpTip } from "../ui/HelpTip";
import { DatabaseIcon } from "../metrics/MetricCards";
import { AgentStrip } from "./AgentStrip";
import { platformLabels, readerOf, serverExplanation, serverStateWords, type ServerEngine } from "./monitoringFormat";
import { PostgresServerPanel } from "./PostgresServerPanel";
import { SqlServerPanel } from "./SqlServerPanel";
import { StateChip } from "./StateChip";
import "./monitoring.css";

/** How often the list asks again, so a state changes on screen without a reload. */
const refreshEvery = 10_000;

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** The server the panel is open for, or null to add one, with what it runs. */
type Editing =
  { engine: "SqlServer"; server: SqlServer | null } | { engine: "PostgreSQL"; server: PostgresServer | null };

/** Props for {@link ServerCard}. */
interface ServerCardProps {
  /** The server with its state. */
  server: MonitoredServer;
  /** What it runs, for its words and its database screen. */
  engine: ServerEngine;
  /** Its kind as the card names it, such as "PostgreSQL" or "Azure SQL Database". */
  kind: string;
  /** Every agent, to name the one that reads it. */
  agents: Agent[];
  /** The current time, for "last figures 8 s ago". */
  now: Date;
  /** Whether its removal waits for a second click. */
  removing: boolean;
  /** Opens the panel to change or fix it. */
  onEdit: () => void;
  /** Asks for the second click, or takes it back with null. */
  onAskRemove: (asking: boolean) => void;
  /** Removes it. */
  onRemove: () => void;
}

/**
 * One server on the page: its name, where it is, its state in words and what to do, and its actions.
 *
 * @param props - The server and what its buttons do.
 * @returns The card.
 */
function ServerCard({ server, engine, kind, agents, now, removing, onEdit, onAskRemove, onRemove }: ServerCardProps) {
  const words = serverStateWords(server.state);
  const reader = readerOf(server, agents);
  const block = `${engine === "PostgreSQL" ? "postgresql" : "microsoft.sql_server"} ${server.name}`;

  return (
    <article className={`monitor-card monitor-server${words.tone === "bad" ? " bad" : ""}`} aria-label={server.name}>
      <span className="monitor-icon">
        <DatabaseIcon />
      </span>
      <div className="monitor-main">
        <b>{server.name}</b>
        <span className="monitor-meta">
          {kind} · {server.address}:{server.port} · {server.environment} · read by {reader}
        </span>
        <span className={`monitor-why ${words.tone}`}>{serverExplanation(server, now, reader, engine)}</span>
      </div>
      <div className="monitor-actions">
        <StateChip label={words.label} tone={words.tone} />
        {server.state === "Receiving" && (
          <Link className="button small" to={databasePath(block)}>
            Open
          </Link>
        )}
        {engine === "SqlServer" && (
          <Link
            className="button small quiet"
            to={integrationPath(server.id)}
            aria-label={`Package page of ${server.name}`}
          >
            Package
          </Link>
        )}
        <button type="button" className={`button small${words.tone === "bad" ? "" : " quiet"}`} onClick={onEdit}>
          {words.tone === "bad" ? "Fix" : "Edit"}
        </button>
        {removing ? (
          <>
            <button type="button" className="button small danger" onClick={onRemove}>
              Remove {server.name}
            </button>
            <button type="button" className="button small quiet" onClick={() => onAskRemove(false)}>
              Keep
            </button>
          </>
        ) : (
          <button type="button" className="button small quiet" onClick={() => onAskRemove(true)}>
            Remove
          </button>
        )}
      </div>
    </article>
  );
}

/**
 * Settings → Database monitoring (ADR 0019): the SQL Servers and PostgreSQL servers (VEY-33) Veyra's agents read,
 * each with its state in words, and the panels to add or change one. Nothing else needs editing: Veyra sends each
 * agent its settings.
 *
 * @returns The page's content, inside the Settings screen.
 */
export function DatabaseMonitoring() {
  const [servers, setServers] = useState<SqlServer[] | null>(null);
  const [postgres, setPostgres] = useState<PostgresServer[]>([]);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);
  const [now, setNow] = useState(() => new Date());
  const addHelpId = useId();

  const load = useCallback(
    () =>
      Promise.all([api.sqlServers(), api.postgresServers(), api.agents()])
        .then(([listed, listedPostgres, known]) => {
          setServers(listed);
          setPostgres(listedPostgres);
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

  async function remove(engine: ServerEngine, id: string) {
    try {
      await (engine === "PostgreSQL" ? api.deletePostgresServer(id) : api.deleteSqlServer(id));
      setRemoving(null);
      await load();
    } catch (failure) {
      setError(messageOf(failure));
    }
  }

  function close() {
    setEditing(null);
    void load();
  }

  // The lists load together, so a loaded server list means the agents are known too.
  const loaded = servers !== null;
  const noAgent = loaded && agents.length === 0;
  const card = (server: MonitoredServer, engine: ServerEngine, kind: string, edit: () => void) => (
    <ServerCard
      key={server.id}
      server={server}
      engine={engine}
      kind={kind}
      agents={agents}
      now={now}
      removing={removing === server.id}
      onEdit={edit}
      onAskRemove={(asking) => setRemoving(asking ? server.id : null)}
      onRemove={() => void remove(engine, server.id)}
    />
  );

  return (
    <div className="monitor">
      {loaded && <AgentStrip agents={agents} now={now} />}

      <div className="monitor-bar">
        <div className="monitor-kinds">
          <span className="monitor-kind on">
            <DatabaseIcon size={16} />
            SQL Server <small>{servers?.length ?? "…"}</small>
          </span>
          <span className="monitor-kind on">
            <DatabaseIcon size={16} />
            PostgreSQL <small>{loaded ? postgres.length : "…"}</small>
          </span>
          <HelpTip label="Database servers">
            Each card below is a server an agent reads. Its chip says how that stands: Receiving means its figures
            arrive, and a red one needs you, with what to do on the card. Open shows the server&apos;s database screen.
          </HelpTip>
        </div>
        <div className="monitor-adds">
          <button
            type="button"
            className="button primary"
            disabled={noAgent}
            aria-describedby={noAgent ? addHelpId : undefined}
            onClick={() => setEditing({ engine: "SqlServer", server: null })}
          >
            + Add SQL Server
          </button>
          <button
            type="button"
            className="button primary"
            disabled={noAgent}
            aria-describedby={noAgent ? addHelpId : undefined}
            onClick={() => setEditing({ engine: "PostgreSQL", server: null })}
          >
            + Add PostgreSQL
          </button>
        </div>
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

      {loaded && servers.length === 0 && postgres.length === 0 && !noAgent && (
        <div className="monitor-card monitor-empty">
          <b>No database server yet</b>
          <p>
            Add a SQL Server or a PostgreSQL server: Veyra writes the script for its login, sends the settings to the
            agent that reads it, and shows here what the agent found. No file to edit, no variable to set.
          </p>
        </div>
      )}

      {servers?.map((server) =>
        card(server, "SqlServer", platformLabels[server.platform], () => setEditing({ engine: "SqlServer", server })),
      )}

      {postgres.map((server) =>
        card(server, "PostgreSQL", "PostgreSQL", () => setEditing({ engine: "PostgreSQL", server })),
      )}

      <div className="monitor-card monitor-rules">
        <div>
          <b>Alerts for these servers</b>
          <HelpTip label="Alerts for these servers">
            Built-in rules judge every server above and open alerts on it. Blocking and Deadlocks watch sessions waiting
            on each other, Long query the longest running query, Backup age the last full backup, and SQL Agent jobs any
            job whose last run failed. Connections and Wraparound are for PostgreSQL.
          </HelpTip>
          <p>Built-in rules judge every server above. Their levels are under Alert rules.</p>
        </div>
        <div className="monitor-chips">
          <span>Blocking</span>
          <span>Deadlocks</span>
          <span>Long query</span>
          <span>Connections</span>
          <span>Wraparound</span>
          <span>Backup age</span>
          <span>SQL Agent jobs</span>
        </div>
        <Link className="button small" to="/settings?section=Rules">
          Alert rules ›
        </Link>
      </div>

      {editing?.engine === "SqlServer" && (
        <SqlServerPanel server={editing.server} agents={agents} onClose={close} onSaved={() => void load()} />
      )}

      {editing?.engine === "PostgreSQL" && (
        <PostgresServerPanel server={editing.server} agents={agents} onClose={close} onSaved={() => void load()} />
      )}
    </div>
  );
}
