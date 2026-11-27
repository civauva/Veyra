import { useId, useState } from "react";
import { Link } from "react-router";

import type {
  Agent,
  IntegrationModuleInfo,
  SqlServerAuthentication,
  SqlServerEncryption,
  SqlServerPlatform,
} from "../../../api/types";
import {
  agentWords,
  authenticationLabels,
  authenticationShort,
  collectionWords,
  integrationPath,
  isWindowsAgent,
  modulesWords,
  probeFailureWords,
  productName,
  readerWords,
  setupScript,
} from "../../../integrations/integrationWords";
import { useInstall, useProbe, useWizardData, type ProbeView } from "../../../integrations/useIntegrations";
import {
  connectionKey,
  draftTarget,
  nameOf,
  newDraft,
  probeInputOf,
  reachableStep,
  serverInputOf,
  stepProblems,
  suggestedName,
  wizardSteps,
  type WizardContext,
  type WizardDraft,
  type WizardStep,
} from "../../../integrations/wizard";
import { formatClock } from "../../../logs/format";
import { encryptionLabels, newPassword, platformLabels, serverStateWords } from "../../../settings/monitoringFormat";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { docs } from "../ConsoleHelp";
import { ConsoleIcon } from "../ConsoleIcon";
import { ModuleRows, ProbeBanner, ScriptBlock } from "./integrationParts";
import "./consoleIntegrations.css";

/** The trail every state of the wizard shows. */
const trail = [
  { label: "Integrations", to: "/integrations" },
  { label: "SQL Server", to: "/integrations" },
  { label: "Add a server" },
];

/**
 * Console's wizard that adds a SQL Server (VEY-78, reference screenshot 2, ADR 0028): Connection with a test through
 * the agent, Permissions with the login script, Modules each with its interval, Validation, and Installation, which
 * saves the server and follows it until its figures arrive. The summary and what happens next stay beside it.
 */
export function ConsoleIntegrationNew() {
  const data = useWizardData();

  return (
    <main className="cn-screen ig">
      <ScreenBar trail={trail} />
      <div className="cn-head">
        <span className="ig-icon" aria-hidden="true">
          <ConsoleIcon name="database" size={22} />
        </span>
        <div className="ig-head-text">
          <h1>Add a SQL Server</h1>
          <p>Veyra&apos;s agent reads the server with read-only queries and sends what it finds over OpenTelemetry.</p>
        </div>
        <div className="cn-head-actions">
          <a className="cn-button" href={`${docs}/integrations/sqlserver.md`} target="_blank" rel="noreferrer noopener">
            <ConsoleIcon name="help" size={14} />
            SQL Server guide
          </a>
        </div>
      </div>
      {data.agents && data.catalog ? (
        <Wizard agents={data.agents} catalog={data.catalog} templateVersion={data.templateVersion} />
      ) : (
        <p className={data.error ? "cn-error" : "cn-empty"} role={data.error ? "alert" : undefined}>
          {data.error ? `Could not read the agents and modules: ${data.error}` : "Reading the agents and modules…"}
        </p>
      )}
    </main>
  );
}

/** Props for {@link Wizard}. */
interface WizardProps {
  /** The agents that could read the server. */
  agents: Agent[];
  /** The template's modules. */
  catalog: IntegrationModuleInfo[];
  /** The template version a new server takes, or null when none says. */
  templateVersion: string | null;
}

/** The wizard itself, once the agents and the modules are known. */
function Wizard({ agents, catalog, templateVersion }: WizardProps) {
  const [draft, setDraft] = useState<WizardDraft>(() => newDraft(agents, catalog));
  const [step, setStep] = useState<WizardStep>("connection");
  const [tried, setTried] = useState<WizardStep | null>(null);
  const probe = useProbe();
  const install = useInstall();
  const context: WizardContext = { agents, catalog };
  const index = wizardSteps.findIndex((one) => one.id === step);
  const reachable = reachableStep(draft, context);
  const problems = stepProblems(draft, step, context);
  const key = connectionKey(draft);
  const fresh = probe.key === key;
  const agent = agents.find((one) => one.id === draft.agentId);
  const agentName = agent
    ? agent.placement === "BuiltIn"
      ? "the built-in agent"
      : readerWords(agent.name)
    : "the agent";
  const saved = install.saved !== null;

  function change(patch: Partial<WizardDraft>) {
    setDraft((current) => ({ ...current, ...patch }));
  }

  function test() {
    if (stepProblems(draft, "connection", context).length > 0) {
      setTried("connection");
      setStep("connection");

      return;
    }

    probe.start(probeInputOf(draft), key);
  }

  function go(next: WizardStep) {
    setTried(null);
    setStep(next);

    // Validation tests the connection as it will be saved, unless the latest test already did.
    if (next === "validation" && !(fresh && (probe.running || probe.probe?.state === "succeeded"))) {
      probe.start(probeInputOf(draft), key);
    }
  }

  function forward() {
    if (problems.length > 0) {
      setTried(step);

      return;
    }

    const next = wizardSteps[index + 1];

    if (next) {
      go(next.id);
    }
  }

  const nextStep = wizardSteps[index + 1];

  return (
    <>
      <nav className="cn-card ig-steps" aria-label="Steps">
        <ol>
          {wizardSteps.map((one, position) => {
            const done = position < index || (saved && position <= index);

            return (
              <li key={one.id} className={[one.id === step && "on", done && "done"].filter(Boolean).join(" ")}>
                <button
                  type="button"
                  aria-current={one.id === step ? "step" : undefined}
                  disabled={(position > reachable && position > index) || saved}
                  onClick={() => go(one.id)}
                >
                  <span className="ig-step-number" aria-hidden="true">
                    {done ? <ConsoleIcon name="check" size={13} /> : position + 1}
                  </span>
                  <b>{one.label}</b>
                </button>
              </li>
            );
          })}
        </ol>
      </nav>

      <div className="ig-columns">
        <div className="ig-main">
          {step === "connection" && (
            <ConnectionStep
              draft={draft}
              agents={agents}
              probe={probe}
              fresh={fresh}
              agentName={agentName}
              onChange={change}
              onTest={test}
            />
          )}
          {step === "permissions" && <PermissionsStep draft={draft} probe={probe} fresh={fresh} />}
          {step === "modules" && (
            <section className="cn-card" aria-labelledby="ig-modules">
              <header>
                <ConsoleIcon name="layers" size={15} />
                <h2 id="ig-modules">3. Modules</h2>
                <HelpTip label="Modules">
                  The template&apos;s reads in groups. Each is read at its own interval; switch off what this server
                  does not need. Core, the server&apos;s performance and health, is always on. Shorter intervals show
                  more but cost the server a little more.
                </HelpTip>
                <span className="cn-meta">each with its own interval</span>
              </header>
              <div className="ig-body">
                <ModuleRows catalog={catalog} modules={draft.modules} onChange={(modules) => change({ modules })} />
              </div>
            </section>
          )}
          {step === "validation" && <ValidationStep draft={draft} probe={probe} fresh={fresh} agentName={agentName} />}
          {step === "installation" && <InstallationStep draft={draft} install={install} onChange={change} />}
          {tried === step && problems.length > 0 && (
            <ul className="ig-problems" role="alert" aria-label="What the step still needs">
              {problems.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
          )}
        </div>

        <aside className="ig-side" aria-label="About the integration">
          <section className="cn-card" aria-labelledby="ig-summary">
            <header>
              <ConsoleIcon name="database" size={15} />
              <h2 id="ig-summary">Integration summary</h2>
              <HelpTip label="Integration summary">
                What will be saved: where the agent connects, how it signs in, which agent reads the server, how many
                modules are on and how often they are read, the template version, and what the server sends.
              </HelpTip>
            </header>
            <dl className="ig-kv">
              <div>
                <dt>Target</dt>
                <dd>{draftTarget(draft)}</dd>
              </div>
              <div>
                <dt>Authentication</dt>
                <dd>{authenticationShort[draft.authentication]}</dd>
              </div>
              <div>
                <dt>Read by</dt>
                <dd>{agent ? (agent.placement === "BuiltIn" ? "Built-in agent" : agent.name) : "—"}</dd>
              </div>
              <div>
                <dt>Modules</dt>
                <dd>{modulesWords(draft.modules)}</dd>
              </div>
              <div>
                <dt>Collection</dt>
                <dd>{collectionWords(draft.modules)}</dd>
              </div>
              <div>
                <dt>Template</dt>
                <dd>{templateVersion ? `sqlserver ${templateVersion}` : "the current one"}</dd>
              </div>
              <div>
                <dt>Telemetry</dt>
                <dd>Metrics · Logs</dd>
              </div>
            </dl>
          </section>
          <section className="cn-card" aria-labelledby="ig-next">
            <header>
              <ConsoleIcon name="help" size={15} />
              <h2 id="ig-next">What happens next</h2>
              <HelpTip label="What happens next">
                After Installation saves the server, Veyra sends its agent a new configuration. Nothing is installed on
                the SQL Server itself; the agent only runs read-only queries with the login.
              </HelpTip>
            </header>
            <ol className="ig-next">
              <li>Veyra writes the agent&apos;s configuration.</li>
              <li>The agent applies it within a minute.</li>
              <li>Validation checks the login and every permission.</li>
              <li>The first figures arrive within two minutes.</li>
            </ol>
          </section>
          <div className="ig-buttons">
            {index === 0 ? (
              <Link className="cn-button" to="/integrations">
                Cancel
              </Link>
            ) : (
              !saved && (
                <button type="button" className="cn-button" onClick={() => go(wizardSteps[index - 1]?.id ?? step)}>
                  ← Back
                </button>
              )
            )}
            {step === "connection" && (
              <button type="button" className="cn-button" disabled={probe.running} onClick={test}>
                <ConsoleIcon name="play" size={12} />
                {probe.running ? "Testing…" : "Test connection"}
              </button>
            )}
            {nextStep && (
              <button type="button" className="cn-button primary" onClick={forward}>
                Next: {nextStep.label.toLowerCase()} →
              </button>
            )}
            {step === "installation" &&
              (saved && install.saved ? (
                <Link className="cn-button primary" to={integrationPath(install.saved.id)}>
                  Open the package page →
                </Link>
              ) : (
                <button
                  type="button"
                  className="cn-button primary"
                  disabled={install.busy}
                  onClick={() => {
                    if (problems.length > 0) {
                      setTried(step);
                    } else {
                      void install.install(serverInputOf(draft));
                    }
                  }}
                >
                  {install.busy ? "Installing…" : "Install"}
                </button>
              ))}
          </div>
        </aside>
      </div>
    </>
  );
}

/** Props for {@link ConnectionStep}. */
interface ConnectionStepProps {
  draft: WizardDraft;
  agents: Agent[];
  probe: ProbeView;
  /** Whether the probe tried the connection as it is now. */
  fresh: boolean;
  /** The agent as a sentence names it. */
  agentName: string;
  onChange(patch: Partial<WizardDraft>): void;
  onTest(): void;
}

/** Step 1: where the server is, how the agent signs in, which agent reads it, and the test. */
function ConnectionStep({ draft, agents, probe, fresh, agentName, onChange, onTest }: ConnectionStepProps) {
  const id = useId();
  const [showPassword, setShowPassword] = useState(false);
  const agent = agents.find((one) => one.id === draft.agentId);
  const windows = isWindowsAgent(agent);
  const named = draft.instanceKind === "named";
  const integrated = draft.authentication === "integrated";
  const authentications: SqlServerAuthentication[] = windows ? ["sql", "ntlm", "integrated"] : ["sql", "ntlm"];
  const finished = fresh && probe.probe?.finishedAt ? probe.probe.finishedAt : null;

  return (
    <section className="cn-card" aria-labelledby={`${id}-title`}>
      <header>
        <ConsoleIcon name="database" size={15} />
        <h2 id={`${id}-title`}>1. Connect to SQL Server</h2>
        <HelpTip label="Connect to SQL Server">
          Where the agent finds the server and how it signs in. A default instance listens on its port, usually 1433; a
          named instance is found through the SQL Server Browser on UDP 1434. Test connection asks the agent to connect
          with these settings before anything is saved.
        </HelpTip>
        {finished && (
          <span className="cn-meta">
            tested {formatClock(finished)} by {agentName}
          </span>
        )}
      </header>
      <div className="ig-form">
        <div className="ig-seg" role="group" aria-label="Instance">
          <button type="button" aria-pressed={!named} onClick={() => onChange({ instanceKind: "default" })}>
            <ConsoleIcon name="database" size={14} />
            Default instance
          </button>
          <button type="button" aria-pressed={named} onClick={() => onChange({ instanceKind: "named" })}>
            <ConsoleIcon name="layers" size={14} />
            Named instance
          </button>
        </div>

        <div className="ig-row">
          <label className="ig-field">
            <span>Server (host)</span>
            <input
              className="ig-input"
              value={draft.address}
              autoComplete="off"
              spellCheck={false}
              placeholder="sql-prod-01.corp.local"
              onChange={(event) => onChange({ address: event.target.value })}
            />
          </label>
          {named ? (
            <label className="ig-field narrow">
              <span>Instance name</span>
              <input
                className="ig-input"
                value={draft.instance}
                autoComplete="off"
                spellCheck={false}
                placeholder="REPORTS"
                onChange={(event) => onChange({ instance: event.target.value })}
              />
            </label>
          ) : (
            <label className="ig-field narrow">
              <span>Port</span>
              <input
                className="ig-input"
                value={draft.port}
                inputMode="numeric"
                onChange={(event) => onChange({ port: event.target.value })}
              />
            </label>
          )}
        </div>

        <label className="ig-field">
          <span>Authentication</span>
          <select
            className="ig-select"
            value={draft.authentication}
            onChange={(event) => onChange({ authentication: event.target.value as SqlServerAuthentication })}
          >
            {authentications.map((option) => (
              <option key={option} value={option}>
                {authenticationLabels[option]}
              </option>
            ))}
          </select>
        </label>

        {integrated ? (
          <label className="ig-field">
            <span>The agent&apos;s service account</span>
            <input
              className="ig-input"
              value={draft.login}
              autoComplete="off"
              spellCheck={false}
              placeholder="CORP\svc-veyra-agent"
              onChange={(event) => onChange({ login: event.target.value })}
            />
            <small>The account the agent&apos;s service runs as; the login script creates it in SQL Server.</small>
          </label>
        ) : (
          <div className="ig-row">
            <label className="ig-field">
              <span>{draft.authentication === "ntlm" ? "Domain login" : "Login"}</span>
              <input
                className="ig-input"
                value={draft.login}
                autoComplete="off"
                spellCheck={false}
                placeholder={draft.authentication === "ntlm" ? "CORP\\veyra_monitor" : "veyra_monitor"}
                onChange={(event) => onChange({ login: event.target.value })}
              />
            </label>
            <label className="ig-field narrow">
              <span>Password</span>
              <span className="ig-password">
                <input
                  className="ig-input"
                  type={showPassword ? "text" : "password"}
                  value={draft.password}
                  autoComplete="new-password"
                  onChange={(event) => onChange({ password: event.target.value })}
                />
                <button
                  type="button"
                  className="ig-icon-button"
                  aria-label={showPassword ? "Hide the password" : "Show the password"}
                  aria-pressed={showPassword}
                  onClick={() => setShowPassword(!showPassword)}
                >
                  <ConsoleIcon name="eye" size={15} />
                </button>
                {draft.authentication === "sql" && (
                  <button
                    type="button"
                    className="ig-icon-button"
                    aria-label="Make a new password"
                    title="Make a new password"
                    onClick={() => onChange({ password: newPassword() })}
                  >
                    <ConsoleIcon name="refresh" size={14} />
                  </button>
                )}
              </span>
            </label>
          </div>
        )}

        <div className="ig-row">
          <label className="ig-field">
            <span>Read by</span>
            <select
              className="ig-select"
              value={draft.agentId}
              onChange={(event) => {
                const next = agents.find((one) => one.id === event.target.value);

                // Only a Windows agent signs in with its own account.
                onChange({
                  agentId: event.target.value,
                  ...(integrated && !isWindowsAgent(next) ? { authentication: "ntlm" as const } : {}),
                });
              }}
            >
              {agents.length === 0 && <option value="">No agent yet</option>}
              {agents.map((one) => (
                <option key={one.id} value={one.id}>
                  {agentWords(one)}
                </option>
              ))}
            </select>
          </label>
          <label className="ig-field narrow">
            <span>Environment</span>
            <input
              className="ig-input"
              value={draft.environment}
              autoComplete="off"
              onChange={(event) => onChange({ environment: event.target.value })}
            />
          </label>
        </div>

        <div className="ig-row">
          <label className="ig-field">
            <span>Kind</span>
            <select
              className="ig-select"
              value={draft.platform}
              onChange={(event) => onChange({ platform: event.target.value as SqlServerPlatform })}
            >
              {(Object.keys(platformLabels) as SqlServerPlatform[]).map((option) => (
                <option key={option} value={option}>
                  {platformLabels[option]}
                </option>
              ))}
            </select>
          </label>
          <label className="ig-field narrow">
            <span>Encryption</span>
            <select
              className="ig-select"
              value={draft.encryption}
              onChange={(event) => onChange({ encryption: event.target.value as SqlServerEncryption })}
            >
              {(Object.keys(encryptionLabels) as SqlServerEncryption[]).map((option) => (
                <option key={option} value={option}>
                  {encryptionLabels[option]}
                </option>
              ))}
            </select>
          </label>
        </div>

        {draft.platform === "AzureSqlDatabase" && (
          <label className="ig-field">
            <span>Database</span>
            <input
              className="ig-input"
              value={draft.database}
              autoComplete="off"
              placeholder="orders"
              onChange={(event) => onChange({ database: event.target.value })}
            />
            <small>Azure SQL Database is read one database at a time.</small>
          </label>
        )}

        <small className="ig-hint">
          Windows authentication: NTLM with a domain login from any agent, or the agent&apos;s own account on a Windows
          agent. A named instance needs UDP 1434 open from the agent.
        </small>

        {(fresh || probe.error) && (
          <ProbeBanner
            probe={fresh ? probe.probe : null}
            running={fresh && probe.running}
            elapsedSeconds={probe.elapsedSeconds}
            agent={agentName}
            error={probe.error}
          />
        )}
      </div>
      <footer>
        <span>The test runs on the agent: its Collector restarts twice for a few seconds</span>
        <button type="button" className="link-button" disabled={probe.running} onClick={onTest}>
          {fresh && probe.probe ? "Test again" : "Test connection"}
        </button>
      </footer>
    </section>
  );
}

/** Props for {@link PermissionsStep}. */
interface PermissionsStepProps {
  draft: WizardDraft;
  probe: ProbeView;
  fresh: boolean;
}

/** Step 2: the login script, and what the latest test found missing. */
function PermissionsStep({ draft, probe, fresh }: PermissionsStepProps) {
  const script = setupScript(draft.authentication, draft.login, draft.password);
  const result = fresh ? probe.probe?.result : null;

  return (
    <section className="cn-card" aria-labelledby="ig-permissions">
      <header>
        <ConsoleIcon name="keys" size={15} />
        <h2 id="ig-permissions">2. Permissions</h2>
        <HelpTip label="Permissions">
          Veyra reads server state, Query Store, backups, and SQL Agent history, and nothing else: no table data. The
          script creates the login with exactly those grants. Only an administrator of the server can run it.
        </HelpTip>
      </header>
      <div className="ig-body">
        <p className="ig-text">
          {draft.authentication === "sql"
            ? "Run this once in SQL Server Management Studio as an administrator. It creates the login with the password from Connection and grants it read-only access."
            : "Run this once in SQL Server Management Studio as an administrator. It creates the Windows login and grants it read-only access."}
        </p>
        <ScriptBlock
          label="Login script"
          text={script}
          secret={draft.authentication === "sql" ? draft.password : undefined}
        />
        <h3 className="ig-subhead">What the test found</h3>
        {result ? (
          result.missingPermissions.length === 0 ? (
            <p className="ig-found ok">
              <ConsoleIcon name="check" size={14} /> The login has every permission Veyra reads with.
            </p>
          ) : (
            <ul className="ig-found bad" aria-label="Missing permissions">
              {result.missingPermissions.map((permission) => (
                <li key={permission}>
                  <ConsoleIcon name="close" size={13} /> {permission}
                </li>
              ))}
            </ul>
          )
        ) : (
          <p className="ig-text sq-muted">
            Test the connection to see which permissions the login lacks. Validation tests it again before saving.
          </p>
        )}
      </div>
    </section>
  );
}

/** Props for {@link ValidationStep}. */
interface ValidationStepProps {
  draft: WizardDraft;
  probe: ProbeView;
  fresh: boolean;
  agentName: string;
}

/** The line a failure stops at: the agent, the network, or the login. */
function failedLine(reason: string | undefined): number {
  switch (reason) {
    case "no-agent":
    case "agent-offline":
    case "timed-out":
      return 0;
    case "login-failed":
      return 2;
    default:
      return 1;
  }
}

/** Step 4: a test of the connection as it will be saved, line by line. */
function ValidationStep({ draft, probe, fresh, agentName }: ValidationStepProps) {
  const current = fresh ? probe.probe : null;
  const result = current?.result ?? null;
  const failedAt = current?.state === "failed" ? failedLine(current.error?.reason) : -1;
  const named = draft.instanceKind === "named";
  const lines = [
    `${agentName.charAt(0).toUpperCase()}${agentName.slice(1)} ran the test`,
    `Reached ${named ? `${draft.address.trim()}\\${draft.instance.trim()}` : `${draft.address.trim()}:${draft.port}`}`,
    `Signed in as ${draft.login.trim()}`,
    result ? `${productName(result.version, result.updateLevel)} · ${result.edition}` : "Read the version and edition",
    result
      ? `${result.databases} databases: ${result.databaseNames.slice(0, 6).join(", ")}${result.databaseNames.length > 6 ? ", …" : ""}`
      : "Listed the databases",
    result
      ? result.missingPermissions.length === 0
        ? "Every permission granted"
        : `Missing: ${result.missingPermissions.join(", ")}`
      : "Checked every permission",
  ];

  const passed = (position: number): boolean | null => {
    if (current?.state === "succeeded") {
      return position === 5 ? (result?.missingPermissions.length ?? 0) === 0 : true;
    }

    if (failedAt >= 0) {
      return position < failedAt ? true : position === failedAt ? false : null;
    }

    return null;
  };

  return (
    <section className="cn-card" aria-labelledby="ig-validation">
      <header>
        <ConsoleIcon name="check" size={15} />
        <h2 id="ig-validation">4. Validation</h2>
        <HelpTip label="Validation">
          The agent connects with the settings as they will be saved: it reaches the server, signs in, reads the version
          and the databases, and checks every permission. Its Collector restarts twice for a few seconds.
        </HelpTip>
        {probe.running && fresh && <span className="cn-meta">running · {Math.floor(probe.elapsedSeconds)} s</span>}
      </header>
      <div className="ig-body">
        <ul className="ig-lines" aria-label="Validation">
          {lines.map((line, position) => {
            const state = passed(position);

            return (
              <li key={position} className={state === null ? "open" : state ? "yes" : "no"}>
                <span aria-hidden="true">{state === null ? "…" : state ? "✓" : "✕"}</span>
                <span>
                  {line}
                  <span className="visually-hidden">
                    {state === null ? ": not known yet" : state ? ": done" : ": failed"}
                  </span>
                </span>
              </li>
            );
          })}
        </ul>
        {current?.state === "failed" && current.error && (
          <p className="ig-text cn-bad">
            {probeFailureWords(current.error.reason).title}: {probeFailureWords(current.error.reason).fix}
          </p>
        )}
        <ProbeBanner
          probe={null}
          running={fresh && probe.running}
          elapsedSeconds={probe.elapsedSeconds}
          agent={agentName}
          error={probe.error}
        />
        <div>
          <button
            type="button"
            className="cn-button"
            disabled={probe.running}
            onClick={() => probe.start(probeInputOf(draft), connectionKey(draft))}
          >
            <ConsoleIcon name="refresh" size={13} />
            Validate again
          </button>
        </div>
      </div>
    </section>
  );
}

/** Props for {@link InstallationStep}. */
interface InstallationStepProps {
  draft: WizardDraft;
  install: ReturnType<typeof useInstall>;
  onChange(patch: Partial<WizardDraft>): void;
}

/** Step 5: the name in Veyra, the save, and the server's state until its figures arrive. */
function InstallationStep({ draft, install, onChange }: InstallationStepProps) {
  const state = install.state ? serverStateWords(install.state) : null;

  return (
    <section className="cn-card" aria-labelledby="ig-installation">
      <header>
        <ConsoleIcon name="play" size={15} />
        <h2 id="ig-installation">5. Installation</h2>
        <HelpTip label="Installation">
          Install saves the server. Veyra writes the agent&apos;s configuration with the modules and intervals, the
          agent applies it within a minute, and the state follows here until the first figures arrive.
        </HelpTip>
      </header>
      <div className="ig-body">
        <label className="ig-field">
          <span>Name in Veyra</span>
          <input
            className="ig-input"
            value={draft.name}
            maxLength={63}
            disabled={install.saved !== null}
            placeholder={suggestedName(draft.address, draft.instanceKind === "named" ? draft.instance : "")}
            onChange={(event) => onChange({ name: event.target.value })}
          />
          <small>Its history, alerts, and screens are filed under {nameOf(draft) || "this name"}.</small>
        </label>
        {install.error && (
          <p className="cn-error" role="alert">
            Could not install: {install.error}
          </p>
        )}
        {install.saved && state ? (
          <div
            className={`ig-banner ${state.tone === "ok" ? "ok" : state.tone === "bad" ? "bad" : "wait"}`}
            role="status"
          >
            {state.tone === "wait" ? (
              <span className="ig-spinner" aria-hidden="true" />
            ) : (
              <span className="ig-banner-icon" aria-hidden="true">
                <ConsoleIcon name={state.tone === "ok" ? "check" : "close"} size={14} />
              </span>
            )}
            <div>
              <b>
                {install.state === "Receiving"
                  ? `Installed: Veyra is watching ${install.saved.name}`
                  : `Saved ${install.saved.name} · ${state.label}`}
              </b>
              <small>
                {install.state === "Receiving"
                  ? "The first figures arrived. Its package page shows the checks, the Collector, and the modules."
                  : "The agent picks up its configuration within a minute. This follows by itself."}
              </small>
            </div>
          </div>
        ) : (
          <p className="ig-text sq-muted">
            Install saves the server with the connection, the modules, and the current template.
          </p>
        )}
        {install.saved && (
          <p className="ig-text">
            <Link className="ig-link" to={integrationPath(install.saved.id)}>
              Open the package page →
            </Link>
          </p>
        )}
      </div>
    </section>
  );
}
