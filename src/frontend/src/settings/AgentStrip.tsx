import { Link } from "react-router";

import type { Agent } from "../api/types";
import { ago } from "./monitoringFormat";

/** Props for {@link AgentStrip}. */
export interface AgentStripProps {
  /** Every agent, as last loaded. */
  agents: Agent[];
  /** The current time, for how long ago an agent last reported. */
  now: Date;
}

/**
 * The line at the top of Database monitoring that says which agent reads the servers and how it stands, so a missing
 * or stopped agent shows before a server is added, not after.
 *
 * @param props - The agents and the time.
 * @returns The line, or a notice when the agent needs attention.
 */
export function AgentStrip({ agents, now }: AgentStripProps) {
  const agent = agents.find((one) => one.placement === "BuiltIn") ?? agents[0];

  // The Agents page explains how the built-in agent runs, with the commands to check and start it.
  const agentsLink = (text: string) => (
    <Link className="link-button" to="/settings?section=Agents">
      {text}
    </Link>
  );

  if (!agent) {
    return (
      <div className="monitor-agent bad" role="status" aria-label="Agent">
        <i aria-hidden="true" />
        <p>
          <b>Veyra has no agent to read database servers.</b> Its built-in agent comes with Veyra's Docker Compose
          deployment, <code>deploy/docker-compose.yml</code>: start Veyra with it, and the agent sets itself up.
        </p>
        {agentsLink("How to start it ›")}
      </div>
    );
  }

  const name = agent.placement === "BuiltIn" ? "the built-in agent" : `the agent ${agent.name}`;
  const Name = name.charAt(0).toUpperCase() + name.slice(1);
  if (agent.state === "Online") {
    return (
      <div className="monitor-agent ok" role="status" aria-label="Agent">
        <i aria-hidden="true" />
        <p>
          Read by {name} · Online{agent.version ? ` · version ${agent.version}` : ""}
        </p>
        {agentsLink("Agents ›")}
      </div>
    );
  }

  const notices: Record<Exclude<Agent["state"], "Online">, { tone: "wait" | "bad"; text: string }> = {
    NotConnected: {
      tone: "wait",
      text: `${Name} is not running yet. Servers added now wait for it. If this stays, check that the agent container runs.`,
    },
    Offline: {
      tone: "bad",
      text: `${Name} last reported ${ago(agent.lastSeenAt, now)}. Servers wait for it; check that the agent container runs.`,
    },
    ConfigurationFailed: {
      tone: "bad",
      text: `${Name} refused its settings and keeps its last ones: ${agent.error ?? "no reason given"}.`,
    },
    Unhealthy: {
      tone: "bad",
      text: `${Name} reports a problem: ${agent.error ?? "no reason given"}.`,
    },
  };
  const notice = notices[agent.state];

  return (
    <div className={`monitor-agent ${notice.tone}`} role="status" aria-label="Agent">
      <i aria-hidden="true" />
      <p>{notice.text}</p>
      {agentsLink(agent.state === "NotConnected" || agent.state === "Offline" ? "How to start it ›" : "Agents ›")}
    </div>
  );
}
