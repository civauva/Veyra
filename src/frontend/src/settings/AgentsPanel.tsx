import { Fragment, useCallback, useEffect, useState } from "react";

import { api } from "../api/client";
import type { Agent } from "../api/types";
import { HostIcon } from "../metrics/MetricCards";
import { AddAgentCard } from "./AddAgentCard";
import { BuiltInAgentGuide } from "./BuiltInAgentGuide";
import { CommandBlock } from "./CommandBlock";
import { HostWatchForm } from "./HostWatchForm";
import { InstallBox } from "./InstallBox";
import { agentStateWords, ago, uninstallCommand, type StateWords } from "./monitoringFormat";
import { StateChip } from "./StateChip";
import "./monitoring.css";

/** How often the list asks again, so a newly started agent shows without a reload. */
const refreshEvery = 10_000;

/** How often it asks while an install box waits for its agent. */
const waitingEvery = 3_000;

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** An install box that is open: which agent, and its code. */
interface OpenInstall {
  /** The agent's ID. */
  agentId: string;
  /** Its name. */
  agentName: string;
  /** Its one-time code. */
  code: string;
  /** When the code stops working, in ISO 8601. */
  expiresAt: string;
}

/**
 * The line under an agent's name: where it runs, what it reads, and its version.
 *
 * @param agent - The agent.
 * @param now - The current time.
 * @returns The line.
 */
function agentLine(agent: Agent, now: Date): string {
  const place = agent.placement === "BuiltIn" ? "Beside Veyra" : "In another network";
  const parts = [
    agent.hostName ? `${place} on ${agent.hostName}` : place,
    agent.servers === 1 ? "reads 1 database server" : `reads ${agent.servers} database servers`,
  ];

  if (agent.version) {
    parts.push(`version ${agent.version}`);
  }

  if (agent.operatingSystem) {
    parts.push(agent.operatingSystem);
  }

  parts.push(agent.lastSeenAt ? `last seen ${ago(agent.lastSeenAt, now)}` : "never connected");

  return parts.join(" · ");
}

/**
 * An agent's state in words; an agent in another network that was never installed says so.
 *
 * @param agent - The agent.
 * @returns Its label and colour.
 */
function wordsOf(agent: Agent): StateWords {
  return agent.installed ? agentStateWords(agent.state) : { label: "Not installed yet", tone: "wait" };
}

/**
 * Settings → Agents (ADR 0019): Veyra's agents, each the official OpenTelemetry Collector that Veyra configures. The
 * built-in agent comes with Veyra; agents for other networks are added here and installed with a one-time code.
 *
 * @returns The page's content, inside the Settings screen.
 */
export function AgentsPanel() {
  const [agents, setAgents] = useState<Agent[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(() => new Date());
  const [install, setInstall] = useState<OpenInstall | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);

  const load = useCallback(
    () =>
      api
        .agents()
        .then((known) => {
          setAgents(known);
          setNow(new Date());
          setError(null);
        })
        .catch((failure: unknown) => setError(messageOf(failure))),
    [],
  );

  // An open install box asks more often, so the agent shows as connected moments after its installer finishes.
  const waiting = install !== null;

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), waiting ? waitingEvery : refreshEvery);

    return () => window.clearInterval(timer);
  }, [load, waiting]);

  async function newCode(agent: Agent) {
    try {
      const code = await api.newAgentCode(agent.id);
      setInstall({ agentId: agent.id, agentName: agent.name, code: code.code, expiresAt: code.expiresAt });
    } catch (failure) {
      setError(messageOf(failure));
    }
  }

  async function remove(agent: Agent) {
    try {
      await api.deleteAgent(agent.id);
      setRemoving(null);

      if (install?.agentId === agent.id) {
        setInstall(null);
      }

      await load();
    } catch (failure) {
      setError(messageOf(failure));
    }
  }

  const builtIn = agents?.find((agent) => agent.placement === "BuiltIn");
  const origin = window.location.origin;

  const installBox = (agentId: string) =>
    install?.agentId === agentId && (
      <InstallBox
        agentName={install.agentName}
        code={install.code}
        expiresAt={install.expiresAt}
        connected={agents?.some((agent) => agent.id === agentId && agent.state === "Online") ?? false}
        onDone={() => setInstall(null)}
      />
    );

  return (
    <div className="monitor">
      {error && (
        <p className="settings-error" role="alert">
          {error}
        </p>
      )}

      {agents?.map((agent) => {
        const words = wordsOf(agent);
        const remote = agent.placement === "Remote";

        return (
          <Fragment key={agent.id}>
            <article
              className={`monitor-card monitor-server${words.tone === "bad" ? " bad" : ""}`}
              aria-label={remote ? agent.name : "Built-in agent"}
            >
              <span className="monitor-icon">
                <HostIcon />
              </span>
              <div className="monitor-main">
                <b>{remote ? agent.name : "Built-in agent"}</b>
                <span className="monitor-meta">{agentLine(agent, now)}</span>
                {agent.error && <span className="monitor-why bad">{agent.error}</span>}
                {!remote && agent.state === "NotConnected" && (
                  <span className="monitor-why wait">
                    Its container starts with Veyra and connects within half a minute. If it stays like this, check that
                    the agent container runs: see below.
                  </span>
                )}
                {remote && !agent.installed && (
                  <span className="monitor-why wait">Install it on a machine in its network with a one-time code.</span>
                )}
                {remote && agent.installed && (
                  <HostWatchForm key={agent.id} agent={agent} onSaved={() => void load()} />
                )}
                {removing === agent.id && (
                  <div className="monitor-remove">
                    <p>
                      Removing it revokes its key: it can no longer connect or send. Then uninstall it on its machine:
                    </p>
                    <CommandBlock label="Uninstall on Windows" text={uninstallCommand("Windows", origin)} />
                    <CommandBlock label="Uninstall on Linux" text={uninstallCommand("Linux", origin)} />
                  </div>
                )}
              </div>
              <div className="monitor-actions">
                <StateChip label={words.label} tone={words.tone} />
                {remote && (!agent.installed || agent.state === "NotConnected" || agent.state === "Offline") && (
                  <button type="button" className="button small" onClick={() => void newCode(agent)}>
                    New code
                  </button>
                )}
                {remote &&
                  (removing === agent.id ? (
                    <>
                      <button type="button" className="button small danger" onClick={() => void remove(agent)}>
                        Remove {agent.name}
                      </button>
                      <button type="button" className="button small quiet" onClick={() => setRemoving(null)}>
                        Keep
                      </button>
                    </>
                  ) : (
                    <button type="button" className="button small quiet" onClick={() => setRemoving(agent.id)}>
                      Remove
                    </button>
                  ))}
              </div>
            </article>
            {installBox(agent.id)}
          </Fragment>
        );
      })}

      {agents && (
        <BuiltInAgentGuide exists={builtIn !== undefined} open={builtIn === undefined || builtIn.state !== "Online"} />
      )}

      {install && !agents?.some((agent) => agent.id === install.agentId) && installBox(install.agentId)}

      <AddAgentCard
        onAdded={(added) => {
          setInstall({
            agentId: added.agent.id,
            agentName: added.agent.name,
            code: added.code,
            expiresAt: added.expiresAt,
          });
          void load();
        }}
      />
    </div>
  );
}
