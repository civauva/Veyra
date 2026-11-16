import { CommandBlock } from "./CommandBlock";

/** The Compose file of Veyra's deployment, as the commands name it from the repository's folder. */
const compose = "docker compose -f deploy/docker-compose.yml";

/** One command of the guide, with what it does. */
interface Command {
  /** What it does, which also names its block and its Copy button. */
  label: string;
  /** The command itself. */
  text: string;
}

/** The commands for an agent that exists: check it, start it, read what it says. */
const running: Command[] = [
  { label: "Check that it runs", text: `${compose} ps agent` },
  { label: "Start it", text: `${compose} up -d --no-deps agent` },
  { label: "See what it says", text: `${compose} logs --tail 50 agent` },
];

/** The command for an installation that has no agent: start Veyra with its whole Compose file. */
const missing: Command[] = [{ label: "Start Veyra with its agent", text: `${compose} up -d --build` }];

/** Props for {@link BuiltInAgentGuide}. */
export interface BuiltInAgentGuideProps {
  /** Whether Veyra has a built-in agent at all. */
  exists: boolean;
  /** Whether the agent needs attention, which opens the guide by itself. */
  open: boolean;
}

/**
 * How the built-in agent runs, on the Agents page: what it is, and the commands to check, start, and read it on
 * Veyra's server. Folded away while the agent runs; open when it needs someone.
 *
 * @param props - Whether the agent exists and whether it needs attention.
 * @returns The guide.
 */
export function BuiltInAgentGuide({ exists, open }: BuiltInAgentGuideProps) {
  return (
    <details className="monitor-card monitor-guide" open={open} aria-label="How the built-in agent runs">
      <summary>How the built-in agent runs</summary>
      <p>
        The built-in agent comes with Veyra's Docker Compose deployment: it is the <code>agent</code> service in{" "}
        <code>deploy/docker-compose.yml</code>, the official OpenTelemetry Collector run by its OpAMP supervisor. Veyra
        sets it up on its first start, gives it its own key, and sends it the settings of every server on Database
        monitoring. There is nothing to configure.
      </p>
      <p>
        {exists
          ? "When it does not show as online, run these on Veyra's server, in the folder Veyra was installed from:"
          : "Veyra runs without it, as when it is started on its own. Start it with its Compose file, on Veyra's server, in the folder Veyra was installed from:"}
      </p>
      <ul className="monitor-commands">
        {(exists ? running : missing).map((command) => (
          <li key={command.label}>
            <span>{command.label}</span>
            <CommandBlock label={command.label} text={command.text} />
          </li>
        ))}
      </ul>
      <p>
        Behind a reverse proxy such as Caddy, add its Compose file too, as at installation. For servers Veyra cannot
        reach, add an agent for their network below.
      </p>
    </details>
  );
}
