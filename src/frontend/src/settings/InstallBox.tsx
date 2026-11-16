import { useId, useState } from "react";

import { CommandBlock } from "./CommandBlock";
import { installCommand, type InstallPlatform } from "./monitoringFormat";

/** Props for {@link InstallBox}. */
export interface InstallBoxProps {
  /** The agent's name. */
  agentName: string;
  /** Its one-time code. */
  code: string;
  /** When the code stops working, in ISO 8601. */
  expiresAt: string;
  /** Whether the agent has connected since, which ends the wait. */
  connected: boolean;
  /** Closes the box. */
  onDone: () => void;
}

/** What each installer does, in a sentence, beside its line. */
const where: Record<InstallPlatform, string> = {
  Windows:
    "Run it in PowerShell as an administrator, on any machine in that network that reaches its SQL Servers. It downloads the official OpenTelemetry Collector and supervisor from GitHub, checks them, and installs the Windows service “Veyra agent”.",
  Linux:
    "Run it as root, on any machine in that network that reaches its SQL Servers. It downloads the official OpenTelemetry Collector and supervisor from GitHub, checks them, and installs the systemd service veyra-agent.",
};

/**
 * How to install an agent in another network: its one-time code and the one line to run there, for Windows or Linux,
 * then a wait that ends when the agent connects. The machine needs to reach Veyra and github.com over HTTPS; nothing
 * connects in.
 *
 * @param props - The agent, its code, and whether it connected.
 * @returns The box.
 */
export function InstallBox({ agentName, code, expiresAt, connected, onDone }: InstallBoxProps) {
  const titleId = useId();
  const panelId = useId();
  const [platform, setPlatform] = useState<InstallPlatform>("Windows");
  const until = new Date(expiresAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

  return (
    <section className="monitor-card monitor-install" aria-labelledby={titleId}>
      <h3 id={titleId}>Install {agentName}</h3>
      <p className="monitor-enroll">
        One-time code <code>{code}</code>
        <span>works once, until {until}</span>
      </p>
      <div className="monitor-tabs" role="tablist" aria-label="System">
        {(["Windows", "Linux"] as const).map((option) => (
          <button
            key={option}
            type="button"
            role="tab"
            aria-selected={platform === option}
            aria-controls={panelId}
            className={platform === option ? "on" : undefined}
            onClick={() => setPlatform(option)}
          >
            {option}
          </button>
        ))}
      </div>
      <div id={panelId} role="tabpanel" aria-label={platform}>
        <p>{where[platform]}</p>
        <CommandBlock label="Install command" text={installCommand(platform, window.location.origin, code)} />
      </div>
      <div className="monitor-install-foot">
        <p className={`monitor-waiting${connected ? " ok" : ""}`} role="status">
          <i aria-hidden="true" />
          {connected
            ? `${agentName} is connected. Choose it under “Read by” when you add a SQL Server in its network.`
            : `Waiting for ${agentName} to connect… This page updates as soon as it does.`}
        </p>
        <button type="button" className="button small" onClick={onDone}>
          Done
        </button>
      </div>
    </section>
  );
}
