import { useEffect, useId, useRef, useState, type FormEvent } from "react";

import { api } from "../api/client";
import type { Agent, PostgresEncryption, PostgresServer, PostgresServerInput } from "../api/types";
import {
  checkLines,
  encryptionLabels,
  newPassword,
  postgresLoginScript,
  postgresPasswordScript,
  readerOf,
  serverStateWords,
} from "./monitoringFormat";
import { StateChip } from "./StateChip";

/** How often the check asks how the saved server stands. */
const checkEvery = 5_000;

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** Props for {@link PostgresServerPanel}. */
export interface PostgresServerPanelProps {
  /** The server to change, or null to add one. */
  server: PostgresServer | null;
  /** The agents that could read it. */
  agents: Agent[];
  /** Closes the panel. */
  onClose: () => void;
  /** Called after each save with the stored server. */
  onSaved: (server: PostgresServer) => void;
}

/**
 * The side panel that adds or changes a PostgreSQL server (VEY-33), in the same three steps as a SQL Server's: where
 * it is, the role Veyra signs in as, and a check. The page makes the role's password and writes the script that
 * creates it with pg_monitor; after saving, the check follows the agent's report live, line by line.
 *
 * @param props - The server, the agents, and what to do on close and save.
 * @returns The panel, over the page.
 */
export function PostgresServerPanel({ server, agents, onClose, onSaved }: PostgresServerPanelProps) {
  const titleId = useId();
  const first = useRef<HTMLInputElement>(null);
  const checkStep = useRef<HTMLElement>(null);
  const builtIn = agents.find((agent) => agent.placement === "BuiltIn");

  const [name, setName] = useState(server?.name ?? "");
  const [address, setAddress] = useState(server?.address ?? "");
  const [port, setPort] = useState(String(server?.port ?? 5432));
  const [environment, setEnvironment] = useState(server?.environment ?? "production");
  const [agentId, setAgentId] = useState(server?.agentId ?? builtIn?.id ?? "");
  const [login, setLogin] = useState(server?.login ?? "veyra_monitor");
  const [password, setPassword] = useState(() => (server ? "" : newPassword()));
  const [changingPassword, setChangingPassword] = useState(server === null);
  const [encryption, setEncryption] = useState<PostgresEncryption>(server?.encryption ?? "Required");
  const [showPassword, setShowPassword] = useState(false);
  const [copied, setCopied] = useState(false);
  const [saved, setSaved] = useState<PostgresServer | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Focus moves into the panel once, when it opens; the list refreshing behind it must not pull the focus back.
  useEffect(() => {
    first.current?.focus();
  }, []);

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", onKey);

    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  // Once saved, the check is what the person waits for. It is the last step, below the fold on most screens.
  const savedId = saved?.id;

  useEffect(() => {
    if (savedId) {
      checkStep.current?.scrollIntoView?.({ block: "start" });
    }
  }, [savedId]);

  // After saving, the check follows the server's state until its figures arrive.
  useEffect(() => {
    if (!saved || saved.state === "Receiving") {
      return;
    }

    const timer = window.setInterval(() => {
      api
        .postgresServers()
        .then((servers) => {
          const found = servers.find((one) => one.id === saved.id);

          if (found) {
            setSaved(found);
          }
        })
        .catch(() => undefined);
    }, checkEvery);

    return () => window.clearInterval(timer);
  }, [saved]);

  const script =
    server && changingPassword ? postgresPasswordScript(login, password) : postgresLoginScript(login, password);
  const shownScript = showPassword ? script : script.replaceAll(password.replaceAll("'", "''"), "•".repeat(12));

  function input(): PostgresServerInput {
    return {
      name: name.trim(),
      address: address.trim(),
      port: Number(port),
      environment: environment.trim(),
      agentId: agentId || null,
      login: login.trim(),
      password: changingPassword ? password : null,
      encryption,
    };
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      const id = saved?.id ?? server?.id;
      const stored = id ? await api.updatePostgresServer(id, input()) : await api.createPostgresServer(input());
      setSaved(stored);
      onSaved(stored);
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  async function copyScript() {
    try {
      await navigator.clipboard.writeText(script);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2_000);
    } catch {
      setError("The browser did not allow copying. Show the password and copy the script by hand.");
    }
  }

  const check = saved ? checkLines(saved, readerOf(saved, agents), "PostgreSQL") : null;
  const words = saved ? serverStateWords(saved.state) : null;

  return (
    <>
      <div className="monitor-shade" aria-hidden="true" onClick={onClose} />
      <aside className="monitor-panel" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <header className="monitor-panel-head">
          <h2 id={titleId}>{server ? `Change ${server.name}` : "Add a PostgreSQL server"}</h2>
          <button type="button" className="button small quiet" onClick={onClose}>
            Close
          </button>
        </header>

        <form className="monitor-panel-body" onSubmit={(event) => void save(event)}>
          <div className="monitor-panel-steps">
            <section className="monitor-step" aria-labelledby={`${titleId}-where`}>
              <span className="monitor-step-number" aria-hidden="true">
                1
              </span>
              <div>
                <h3 id={`${titleId}-where`}>Where it is</h3>
                <div className="monitor-fields">
                  <label className="monitor-field">
                    <span>Name in Veyra</span>
                    <input
                      ref={first}
                      value={name}
                      required
                      maxLength={63}
                      placeholder="pg-main"
                      onChange={(event) => setName(event.target.value)}
                    />
                    <small>Its history is filed under this name</small>
                  </label>
                  <label className="monitor-field wide">
                    <span>Address</span>
                    <input
                      value={address}
                      required
                      placeholder="pg-main.corp.local"
                      onChange={(event) => setAddress(event.target.value)}
                    />
                  </label>
                  <label className="monitor-field narrow">
                    <span>Port</span>
                    <input
                      value={port}
                      required
                      inputMode="numeric"
                      pattern="[0-9]*"
                      onChange={(event) => setPort(event.target.value)}
                    />
                  </label>
                </div>
                <div className="monitor-fields">
                  <label className="monitor-field">
                    <span>Environment</span>
                    <input value={environment} required onChange={(event) => setEnvironment(event.target.value)} />
                  </label>
                  <label className="monitor-field wide">
                    <span>Read by</span>
                    <select value={agentId} onChange={(event) => setAgentId(event.target.value)}>
                      {agents.length === 0 && <option value="">No agent yet</option>}
                      {agents.map((agent) => (
                        <option key={agent.id} value={agent.id}>
                          {agent.placement === "BuiltIn" ? "Built-in agent, beside Veyra" : agent.name}
                        </option>
                      ))}
                    </select>
                    <small>The agent connects to the server from where it runs</small>
                  </label>
                </div>
              </div>
            </section>

            <section className="monitor-step" aria-labelledby={`${titleId}-login`}>
              <span className="monitor-step-number" aria-hidden="true">
                2
              </span>
              <div>
                <h3 id={`${titleId}-login`}>The role Veyra uses</h3>
                {changingPassword ? (
                  <>
                    <p>
                      {server
                        ? "Run this once in psql as a superuser: it gives the role its new password."
                        : "Veyra reads the server's statistics and every session's state through pg_monitor, never a table's rows. Run this once in psql as a superuser; it creates the role with a password made here. pg_hba.conf must let the agent in, as the script's last line says."}
                    </p>
                    <div className="monitor-code">
                      <button type="button" className="link-button" onClick={() => void copyScript()}>
                        {copied ? "Copied" : "Copy"}
                      </button>
                      <pre aria-label="Role script">{shownScript}</pre>
                    </div>
                  </>
                ) : (
                  <p>
                    The stored password stays.{" "}
                    <button
                      type="button"
                      className="link-button"
                      onClick={() => {
                        setPassword(newPassword());
                        setChangingPassword(true);
                      }}
                    >
                      Give the role a new password
                    </button>
                  </p>
                )}
                <div className="monitor-fields">
                  <label className="monitor-field">
                    <span>Role</span>
                    <input value={login} required maxLength={63} onChange={(event) => setLogin(event.target.value)} />
                  </label>
                  {changingPassword && (
                    <label className="monitor-field wide">
                      <span>Password</span>
                      <span className="monitor-password">
                        <input
                          type={showPassword ? "text" : "password"}
                          value={password}
                          required
                          autoComplete="new-password"
                          onChange={(event) => setPassword(event.target.value)}
                        />
                        <button
                          type="button"
                          className="button small quiet"
                          aria-pressed={showPassword}
                          onClick={() => setShowPassword(!showPassword)}
                        >
                          {showPassword ? "Hide" : "Show"}
                        </button>
                        <button type="button" className="button small quiet" onClick={() => setPassword(newPassword())}>
                          New
                        </button>
                      </span>
                      <small>
                        Made here, or the role's own without spaces or quotes. Kept encrypted; never shown again
                      </small>
                    </label>
                  )}
                </div>
                <span className="monitor-label" aria-hidden="true">
                  Encryption
                </span>
                <fieldset className="monitor-segments">
                  <legend className="visually-hidden">Encryption</legend>
                  {(Object.keys(encryptionLabels) as PostgresEncryption[]).map((option) => (
                    <label key={option} className={encryption === option ? "on" : undefined}>
                      <input
                        type="radio"
                        name={`${titleId}-encryption`}
                        value={option}
                        checked={encryption === option}
                        onChange={() => setEncryption(option)}
                      />
                      {encryptionLabels[option]}
                    </label>
                  ))}
                </fieldset>
                <p className="monitor-hint">
                  Required checks the server's certificate and name. Choose “Trust its certificate” for a self-signed
                  one, and Off only for a server without TLS on a network you trust.
                </p>
              </div>
            </section>

            <section ref={checkStep} className="monitor-step" aria-labelledby={`${titleId}-check`}>
              <span className="monitor-step-number" aria-hidden="true">
                3
              </span>
              <div>
                <h3 id={`${titleId}-check`}>Check it</h3>
                {check && words ? (
                  <>
                    <p className="monitor-check-state" role="status">
                      <StateChip label={words.label} tone={words.tone} />
                      {saved?.state === "Receiving"
                        ? "Veyra is watching this server."
                        : "The agent picks up the settings within half a minute. This updates by itself."}
                    </p>
                    <ul className="monitor-checks">
                      {check.map((line) => (
                        <li key={line.text} className={line.passed === null ? "open" : line.passed ? "yes" : "no"}>
                          <span aria-hidden="true">{line.passed === null ? "…" : line.passed ? "✓" : "✕"}</span>
                          <span>
                            {line.text}
                            <span className="visually-hidden">
                              {line.passed === null ? ": not known yet" : line.passed ? ": done" : ": failed"}
                            </span>
                            {line.note && <small> {line.note}</small>}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </>
                ) : (
                  <p>
                    After saving, the agent connects with these settings and reports back here: whether it reached the
                    server, signed in, and read every figure.
                  </p>
                )}
              </div>
            </section>

            {error && (
              <p className="settings-error" role="alert">
                {error}
              </p>
            )}
          </div>

          <footer className="monitor-panel-foot">
            <button type="button" className="button quiet" onClick={onClose}>
              {saved ? "Done" : "Cancel"}
            </button>
            <button type="submit" className="button primary" disabled={busy}>
              {saved ? "Save again" : server ? "Save changes" : "Save and start watching"}
            </button>
          </footer>
        </form>
      </aside>
    </>
  );
}
