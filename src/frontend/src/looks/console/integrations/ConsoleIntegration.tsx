import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";

import type { IntegrationDetails, IntegrationModule, Probe } from "../../../api/types";
import { formatBytes } from "../../../home/homeFormat";
import {
  authenticationShort,
  configWords,
  elapsedWords,
  grantsScript,
  historyWords,
  intervalWords,
  probeFacts,
  probeFailureWords,
  readerWords,
  runTimeWords,
  uptimeWords,
} from "../../../integrations/integrationWords";
import { useIntegration, useModuleCatalog, type IntegrationView } from "../../../integrations/useIntegrations";
import { LineChart } from "../../../metrics/LineChart";
import { useServerOverview } from "../../../metrics/database/useServerOverview";
import { formatClockTime } from "../../../metrics/metricFormat";
import { rangeOf, rangeWords } from "../../../metrics/metricRange";
import { MiniLine } from "../../../resources/MiniLine";
import { serverStateWords } from "../../../settings/monitoringFormat";
import { HelpTip } from "../../../ui/HelpTip";
import { ScreenBar } from "../../../ui/ScreenBar";
import { lineOver } from "../database/sqlParts";
import { ConsoleIcon, type ConsoleIconName } from "../ConsoleIcon";
import { BigState, ModuleRows, ScriptBlock } from "./integrationParts";
import "./consoleIntegrations.css";

/** The package page's tabs. */
const tabs = [
  { id: "overview", label: "Overview" },
  { id: "configuration", label: "Configuration" },
  { id: "permissions", label: "Permissions" },
  { id: "history", label: "History" },
] as const;

/** One of {@link tabs}. */
type TabId = (typeof tabs)[number]["id"];

/** The engine SQL Server's figures are filed under. */
const engine = "microsoft.sql_server";

/** Each module's picture on the Collected metrics tiles. */
const moduleIcons: Record<string, ConsoleIconName> = {
  core: "metrics",
  activity: "users",
  queries: "search",
  storage: "database",
  backups: "check",
  jobs: "jobs",
  events: "alerts",
  configuration: "settings",
  indexes: "layers",
};

/**
 * Console's package page of a SQL Server integration (VEY-78, reference screenshot 4, ADR 0028): the template and its
 * version, the Collector and its health, configuration validation with Validate now, the metrics each module
 * collected, the checks with the time SQL Server spends on each, the server's key health, the package's facts, Update,
 * Roll back, Remove, and the next steps; then Configuration, Permissions, and History.
 */
export function ConsoleIntegration() {
  const { id = "" } = useParams();
  const view = useIntegration(id);
  const trail = (label: string) => [{ label: "Integrations", to: "/integrations" }, { label }];

  if (view.notFound) {
    return (
      <main className="cn-screen ig">
        <ScreenBar trail={trail("Not found")} />
        <p className="cn-empty">Veyra monitors no such SQL Server. It may have been removed.</p>
      </main>
    );
  }

  if (!view.details) {
    return (
      <main className="cn-screen ig">
        <ScreenBar trail={trail("…")} />
        <p className="cn-empty">
          {view.error ? `Could not read the integration: ${view.error}` : "Reading the integration…"}
        </p>
      </main>
    );
  }

  return <PackagePage view={view} details={view.details} trail={trail(view.details.integration.name)} />;
}

/** Props for {@link PackagePage}. */
interface PackagePageProps {
  view: IntegrationView;
  details: IntegrationDetails;
  trail: { label: string; to?: string }[];
}

/** The page, once the integration is read. */
function PackagePage({ view, details, trail }: PackagePageProps) {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const [removing, setRemoving] = useState(false);
  const { integration, collector } = details;
  const pack = details.package;
  const tab: TabId = tabs.find((one) => one.id === params.get("tab"))?.id ?? "overview";
  const state = serverStateWords(integration.state);
  const enabled = details.modules.filter((module) => module.enabled);

  function go(next: TabId) {
    setParams(
      (current) => {
        const changed = new URLSearchParams(current);

        if (next === "overview") {
          changed.delete("tab");
        } else {
          changed.set("tab", next);
        }

        return changed;
      },
      { replace: true },
    );
  }

  async function remove() {
    if (await view.remove()) {
      void navigate("/integrations");
    }
  }

  return (
    <main className="cn-screen ig">
      <ScreenBar trail={trail} />
      <div className="cn-head">
        <span className="ig-icon" aria-hidden="true">
          <ConsoleIcon name="database" size={22} />
        </span>
        <div className="ig-head-text">
          <h1>
            {pack.name}
            <span className="cn-pill info ig-version">{pack.version}</span>
          </h1>
          <p>
            {integration.name} · read by {readerWords(collector.agentName)} · {integration.environment}
          </p>
          <ul className="ig-chips" aria-label="Modules on">
            {enabled.map((module) => (
              <li key={module.id}>{module.name.replace(/ and blocking$/, "")}</li>
            ))}
          </ul>
        </div>
        <div className="cn-head-actions">
          <span className={`cn-pill ${state.tone === "wait" ? "info" : state.tone} ig-status`}>
            <i aria-hidden="true" />
            {integration.state === "Receiving" ? `Installed · ${pack.version} active` : state.label}
          </span>
          <button type="button" className="cn-button" onClick={() => go("configuration")}>
            <ConsoleIcon name="settings" size={14} />
            Edit
          </button>
        </div>
      </div>

      <nav className="cn-tabs sq-tabs" aria-label="Integration">
        {tabs.map((one) => (
          <button
            key={one.id}
            type="button"
            aria-current={tab === one.id ? "page" : undefined}
            onClick={() => go(one.id)}
          >
            {one.label}
          </button>
        ))}
      </nav>

      {(view.error || view.actionError) && (
        <p className="cn-error" role="alert">
          {view.actionError ?? `Could not refresh: ${view.error}`}
        </p>
      )}

      {tab === "overview" && <Overview view={view} details={details} onRemove={() => setRemoving(true)} />}
      {tab === "configuration" && <Configuration view={view} details={details} />}
      {tab === "permissions" && <Permissions details={details} />}
      {tab === "history" && <History details={details} />}

      {removing && (
        <RemoveDialog
          name={integration.name}
          busy={view.busy}
          onCancel={() => setRemoving(false)}
          onRemove={() => void remove()}
        />
      )}
    </main>
  );
}

/** Props of the tabs. */
interface TabProps {
  view: IntegrationView;
  details: IntegrationDetails;
}

/** The Overview tab, as reference screenshot 4. */
function Overview({ view, details, onRemove }: TabProps & { onRemove(): void }) {
  const [params] = useSearchParams();
  const range = rangeOf(params);
  const { integration, collector } = details;
  const pack = details.package;
  const config = configWords(collector.configState);
  const probe: Probe | null = view.validation.probe ?? details.validation.lastProbe;
  const lastCheck = probe?.finishedAt ?? null;
  const validationTone = probe?.state === "failed" ? "bad" : config.tone;
  const shownModules = details.modules.filter((module) => module.enabled);
  const behind = pack.version !== pack.latestVersion;

  return (
    <>
      <div className="ig-row-three">
        <section className="cn-card" aria-labelledby="ig-installation">
          <header>
            <ConsoleIcon name="check" size={15} />
            <h2 id="ig-installation">Installation</h2>
            <HelpTip label="Installation">
              The template version this server is read with, and when and by whom it was set. A new server takes the
              current version; Update and Roll back move it.
            </HelpTip>
          </header>
          <div className="ig-big-row">
            <BigState
              tone={integration.state === "Receiving" ? "ok" : serverStateWords(integration.state).tone}
              title={integration.state === "Receiving" ? "Installed" : serverStateWords(integration.state).label}
              detail={`Set ${formatClockTime(pack.setAt)}${pack.setBy ? ` by ${pack.setBy}` : ""}`}
            />
            <span className="cn-pill off mono">{pack.version}</span>
          </div>
        </section>
        <section className="cn-card" aria-labelledby="ig-collector">
          <header>
            <ConsoleIcon name="metrics" size={15} />
            <h2 id="ig-collector">Collector service</h2>
            <HelpTip label="Collector service">
              The agent&apos;s OpenTelemetry Collector as OpAMP reports it: whether it is healthy, its version, and how
              long it has run since it last started.
            </HelpTip>
          </header>
          <div className="ig-big-row">
            <BigState
              tone={collector.healthy ? "ok" : "bad"}
              title={collector.healthy ? "Healthy" : "Unhealthy"}
              detail={`OpenTelemetry Collector ${pack.collectorVersion ?? "—"} · up ${uptimeWords(collector.uptimeSeconds)}`}
            />
            <span className={collector.healthy ? "cn-pill ok" : "cn-pill bad"}>
              {collector.healthy ? "Running" : "Stopped"}
            </span>
          </div>
        </section>
        <section className="cn-card" aria-labelledby="ig-validation">
          <header>
            <ConsoleIcon name="check" size={15} />
            <h2 id="ig-validation">Configuration validation</h2>
            <HelpTip label="Configuration validation">
              Whether the agent applied the configuration Veyra sent, and the last test of the login and its
              permissions. Validate now runs that test on the agent; its Collector restarts twice for a few seconds.
            </HelpTip>
          </header>
          <div className="ig-big-row">
            {view.validation.running ? (
              <BigState
                tone="wait"
                title="Validating…"
                detail={`${elapsedWords(view.validation.elapsedSeconds)} so far, up to 2 minutes`}
              />
            ) : (
              <BigState
                tone={validationTone}
                title={
                  probe?.state === "failed" && probe.error ? probeFailureWords(probe.error.reason).title : config.title
                }
                detail={
                  collector.configError ??
                  `${collector.configState === "applied" ? "Applied" : config.title} · last check ${lastCheck ? formatClockTime(lastCheck) : "never"}`
                }
              />
            )}
            <button
              type="button"
              className="cn-button ig-validate"
              disabled={view.validation.running}
              onClick={() => view.validate()}
            >
              <ConsoleIcon name="refresh" size={14} />
              Validate now
            </button>
          </div>
          {/* What a check found shows once it ran here; the last stored one is summed up above. */}
          {view.validation.probe?.result && !view.validation.running && (
            <p className="ig-card-note">{probeFacts(view.validation.probe.result)}</p>
          )}
        </section>
      </div>

      <div className="ig-row-main">
        <section className="cn-card" aria-labelledby="ig-collected">
          <header>
            <ConsoleIcon name="metrics" size={15} />
            <h2 id="ig-collected">Collected metrics</h2>
            <HelpTip label="Collected metrics">
              The metric series each switched-on module wrote in the time range, and its points over the range. A flat
              or empty line means the module reads nothing: check its permission or the agent.
            </HelpTip>
            <span className="cn-meta">{rangeWords(range)}</span>
          </header>
          <div className="ig-tiles">
            {shownModules.map((module) => (
              <div key={module.id} className="ig-tile">
                <ConsoleIcon name={moduleIcons[module.id] ?? "layers"} size={17} />
                <div>
                  <b>{module.name}</b>
                  <small>
                    {module.series} {module.series === 1 ? "metric" : "metrics"} · every{" "}
                    {intervalWords(module.intervalSeconds)}
                  </small>
                </div>
                <MiniLine values={module.line} tone={module.series > 0 ? "ok" : "bad"} className="ig-tile-line" />
              </div>
            ))}
          </div>
        </section>
        <section className="cn-card" aria-labelledby="ig-checks">
          <header>
            <ConsoleIcon name="check" size={15} />
            <h2 id="ig-checks">Active checks</h2>
            <HelpTip label="Active checks">
              The integration&apos;s reads with the time SQL Server spends on each run, from its statement statistics. A
              red dot is a read that fails.
            </HelpTip>
            <span className="cn-meta">time SQL Server spends per run</span>
          </header>
          <dl className="ig-checks">
            {details.checks.map((check) => (
              <div key={check.name}>
                <dt>
                  <i className={check.ok ? "ok" : "bad"} aria-label={check.ok ? "works" : "fails"} />
                  {check.name} <small>{check.module}</small>
                </dt>
                <dd className="mono">{runTimeWords(check.secondsPerRun)}</dd>
              </div>
            ))}
          </dl>
        </section>
      </div>

      <Health name={integration.name} />

      <div className="ig-row-three">
        <section className="cn-card" aria-labelledby="ig-package">
          <header>
            <ConsoleIcon name="help" size={15} />
            <h2 id="ig-package">Package information</h2>
            <HelpTip label="Package information">
              The template version and the one before it, the Collector versions the template works with, the least SQL
              Server version it supports, and the login with whether it lacks a permission.
            </HelpTip>
          </header>
          <dl className="ig-kv">
            <div>
              <dt>Version</dt>
              <dd>
                {pack.version}
                {pack.previousVersion && pack.previousVersion !== pack.version
                  ? ` (previous ${pack.previousVersion})`
                  : ""}
              </dd>
            </div>
            <div>
              <dt>Collector compatibility</dt>
              <dd>{pack.collectorCompatibility}</dd>
            </div>
            <div>
              <dt>Required SQL Server</dt>
              <dd>{pack.minimumSqlServer}+</dd>
            </div>
            <div>
              <dt>Monitoring login</dt>
              <dd>
                {pack.login}{" "}
                {pack.loginLacks.length === 0 ? (
                  <span className="ig-ok">(least privilege) ✓</span>
                ) : (
                  <span className="cn-bad">lacks {pack.loginLacks.length}</span>
                )}
              </dd>
            </div>
          </dl>
        </section>
        <section className="cn-card" aria-labelledby="ig-actions">
          <header>
            <ConsoleIcon name="settings" size={15} />
            <h2 id="ig-actions">Actions</h2>
            <HelpTip label="Actions">
              Update moves the server to the template Veyra ships now, Roll back to the one before; the agent applies it
              within a minute and the history records it. Remove stops reading the server; what it sent stays until
              retention removes it.
            </HelpTip>
          </header>
          <div className="ig-actions">
            <button
              type="button"
              className="cn-button"
              disabled={view.busy || !behind}
              onClick={() => void view.setVersion(pack.latestVersion)}
            >
              {behind ? `Update to ${pack.latestVersion}` : `Update to ${pack.latestVersion} · current`}
            </button>
            {pack.previousVersion && pack.previousVersion !== pack.version && (
              <button
                type="button"
                className="cn-button ig-rollback"
                disabled={view.busy}
                onClick={() => pack.previousVersion && void view.setVersion(pack.previousVersion)}
              >
                Roll back to {pack.previousVersion}
              </button>
            )}
            <button type="button" className="cn-button ig-remove" disabled={view.busy} onClick={onRemove}>
              Remove integration
            </button>
          </div>
        </section>
        <section className="cn-card" aria-labelledby="ig-steps-next">
          <header>
            <ConsoleIcon name="check" size={15} />
            <h2 id="ig-steps-next">Next steps</h2>
            <HelpTip label="Next steps">
              What the installation still lacks: alert thresholds left at their defaults, no notification channel, and
              retention. The server ticks each off once it is done.
            </HelpTip>
          </header>
          <ul className="ig-todo">
            {details.nextSteps.map((step) => (
              <li key={step.id} className={step.done ? "done" : undefined}>
                <span className="ig-tick" aria-hidden="true">
                  {step.done && <ConsoleIcon name="check" size={11} />}
                </span>
                <span className="visually-hidden">{step.done ? "Done: " : "To do: "}</span>
                {step.link && !step.done ? <Link to={step.link}>{step.text}</Link> : <span>{step.text}</span>}
              </li>
            ))}
          </ul>
        </section>
      </div>
    </>
  );
}

/** The server's key health over the top bar's range: CPU, waits, and size, from its overview read (ADR 0025). */
function Health({ name }: { name: string }) {
  const [params] = useSearchParams();
  const range = rangeOf(params);
  const { data, notFound, error } = useServerOverview(engine, name, range, false);
  const serverPath = `/metrics/databases/${engine}/${encodeURIComponent(name)}`;

  return (
    <section className="cn-card" aria-labelledby="ig-health">
      <header>
        <ConsoleIcon name="metrics" size={15} />
        <h2 id="ig-health">Key SQL Server health</h2>
        <HelpTip label="Key SQL Server health">
          What the Core and Storage modules read, over the range: SQL Server&apos;s share of the CPU, the time sessions
          spent waiting each second with idle waits left out, and the size of every database together. The server&apos;s
          screen has the rest.
        </HelpTip>
        <Link className="cn-meta ig-meta-link" to={serverPath}>
          Open the server →
        </Link>
      </header>
      {data ? (
        <div className="ig-health">
          <div>
            <LineChart
              title="CPU"
              format={(value) => `${Math.round(value * 100)}%`}
              max={1}
              height={130}
              series={[
                {
                  line: lineOver("CPU", data.performance.cpu, data.from, data.to, data.performance.starts),
                  color: "var(--accent)",
                  filled: true,
                },
              ]}
            />
          </div>
          <div>
            <LineChart
              title="Waits"
              format={(value) => `${Math.round(value).toLocaleString("en")} ms/s`}
              height={130}
              series={[
                {
                  line: lineOver(
                    "Waits",
                    data.performance.waitMsPerSecond,
                    data.from,
                    data.to,
                    data.performance.starts,
                  ),
                  color: "var(--warning-fg)",
                },
              ]}
            />
          </div>
          <div>
            <LineChart
              title="Size"
              format={(value) => formatBytes(value)}
              height={130}
              series={[
                {
                  line: lineOver("Size", data.figures.size.line, data.from, data.to),
                  color: "var(--info-fg)",
                  filled: true,
                },
              ]}
            />
          </div>
        </div>
      ) : (
        <p className="cn-empty">
          {notFound
            ? `${name} has sent nothing yet in the range.`
            : error
              ? `Could not read the server's figures: ${error}`
              : "Reading the server's figures…"}
        </p>
      )}
    </section>
  );
}

/** The Configuration tab: each module's switch and interval, saved together. */
function Configuration({ view, details }: TabProps) {
  const { catalog, error } = useModuleCatalog();
  const stored: IntegrationModule[] = details.modules.map((module) => ({
    id: module.id,
    enabled: module.enabled,
    intervalSeconds: module.intervalSeconds,
  }));
  const [modules, setModules] = useState<IntegrationModule[] | null>(null);
  const [saved, setSaved] = useState(false);
  const shown = modules ?? stored;
  const changed = modules !== null && JSON.stringify(modules) !== JSON.stringify(stored);
  const pack = details.package;

  async function save() {
    if (modules && (await view.saveModules(modules))) {
      setModules(null);
      setSaved(true);
    }
  }

  return (
    <div className="ig-row-main">
      <section className="cn-card" aria-labelledby="ig-config-modules">
        <header>
          <ConsoleIcon name="layers" size={15} />
          <h2 id="ig-config-modules">Modules</h2>
          <HelpTip label="Modules">
            Each module&apos;s switch and interval. Saving sends the agent a new configuration; it applies it within a
            minute and the history records the change. Core is always on.
          </HelpTip>
          <span className="cn-meta">each with its own interval</span>
        </header>
        <div className="ig-body">
          {catalog ? (
            <ModuleRows
              catalog={catalog}
              modules={shown}
              disabled={view.busy}
              onChange={(next) => {
                setModules(next);
                setSaved(false);
              }}
            />
          ) : (
            <p className="cn-empty">{error ? `Could not read the modules: ${error}` : "Reading the modules…"}</p>
          )}
        </div>
        <footer className="ig-foot">
          <span role="status">{saved ? "Saved. The agent applies it within a minute." : ""}</span>
          <span className="ig-foot-buttons">
            <button
              type="button"
              className="cn-button"
              disabled={!changed || view.busy}
              onClick={() => setModules(null)}
            >
              Undo
            </button>
            <button
              type="button"
              className="cn-button primary"
              disabled={!changed || view.busy}
              onClick={() => void save()}
            >
              Save changes
            </button>
          </span>
        </footer>
      </section>
      <section className="cn-card" aria-labelledby="ig-connection">
        <header>
          <ConsoleIcon name="database" size={15} />
          <h2 id="ig-connection">Connection</h2>
          <HelpTip label="Connection">
            How the agent reaches and signs in to the server. Change the host, the login, or the password on the
            Settings screen&apos;s database monitoring.
          </HelpTip>
        </header>
        <dl className="ig-kv">
          <div>
            <dt>Read by</dt>
            <dd>{details.collector.agentName}</dd>
          </div>
          <div>
            <dt>Environment</dt>
            <dd>{details.integration.environment}</dd>
          </div>
          <div>
            <dt>Authentication</dt>
            <dd>{authenticationShort[pack.authentication]}</dd>
          </div>
          <div>
            <dt>Login</dt>
            <dd>{pack.login}</dd>
          </div>
        </dl>
        <p className="ig-card-note">
          <Link className="ig-link" to="/settings?section=Databases">
            Change the connection in Settings →
          </Link>
        </p>
      </section>
    </div>
  );
}

/** The Permissions tab: what the login lacks, and the grants to run. */
function Permissions({ details }: { details: IntegrationDetails }) {
  const pack = details.package;

  return (
    <section className="cn-card" aria-labelledby="ig-grants">
      <header>
        <ConsoleIcon name="keys" size={15} />
        <h2 id="ig-grants">Permissions</h2>
        <HelpTip label="Permissions">
          The permissions the login {pack.login} lacks, from the permissions read every module runs, and the grants that
          give them. Only an administrator of the server can run the script.
        </HelpTip>
      </header>
      <div className="ig-body">
        {pack.loginLacks.length === 0 ? (
          <p className="ig-found ok">
            <ConsoleIcon name="check" size={14} /> {pack.login} has every permission Veyra reads with, and no more.
          </p>
        ) : (
          <ul className="ig-found bad" aria-label="Missing permissions">
            {pack.loginLacks.map((permission) => (
              <li key={permission}>
                <ConsoleIcon name="close" size={13} /> {permission}
              </li>
            ))}
          </ul>
        )}
        <p className="ig-text">
          Run this in SQL Server Management Studio as an administrator to give the login every grant again. It changes
          nothing for grants it already has.
        </p>
        <ScriptBlock label="Grants script" text={grantsScript(pack.login)} />
      </div>
    </section>
  );
}

/** The History tab: version changes and other changes of the integration, newest first. */
function History({ details }: { details: IntegrationDetails }) {
  return (
    <section className="cn-card" aria-labelledby="ig-history">
      <header>
        <ConsoleIcon name="calendar" size={15} />
        <h2 id="ig-history">History</h2>
        <HelpTip label="History">
          The monitoring history of this server: template updates and roll backs with their versions, module changes,
          and when it was added. Kept with the server.
        </HelpTip>
      </header>
      {details.history.length === 0 ? (
        <p className="cn-empty">Nothing has changed since the server was added.</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">What</th>
                <th scope="col" className="wide">
                  Who
                </th>
              </tr>
            </thead>
            <tbody>
              {details.history.map((item, index) => (
                <tr key={`${item.at}-${index}`}>
                  <td className="sq-muted">{formatClockTime(item.at)}</td>
                  <td>
                    {historyWords(item)}
                    <span className="cn-table-sub ig-phone-only">{item.by ?? "Veyra"}</span>
                  </td>
                  <td className="wide">{item.by ?? "Veyra"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** Props for {@link RemoveDialog}. */
interface RemoveDialogProps {
  name: string;
  busy: boolean;
  onCancel(): void;
  onRemove(): void;
}

/** Asks before removing: Veyra stops reading the server, and its data stays until retention removes it. */
function RemoveDialog({ name, busy, onCancel, onRemove }: RemoveDialogProps) {
  const keep = useRef<HTMLButtonElement>(null);
  const cancel = useRef(onCancel);

  useEffect(() => {
    cancel.current = onCancel;
  }, [onCancel]);

  // Focus goes to Keep once, when the dialog opens: the page refreshing behind it must not pull it back.
  useEffect(() => {
    keep.current?.focus();

    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") {
        cancel.current();
      }
    }

    window.addEventListener("keydown", onKey);

    return () => window.removeEventListener("keydown", onKey);
  }, []);

  return (
    <div className="cn-dialog-scrim" onClick={onCancel}>
      <div
        className="cn-dialog ig-dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="ig-remove-title"
        aria-describedby="ig-remove-text"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="ig-remove-title">Remove {name}?</h2>
        <p id="ig-remove-text">
          Veyra stops reading this server and sends its agent a configuration without it. What it sent stays until
          retention removes it. Adding it again starts a new history.
        </p>
        <div className="ig-dialog-buttons">
          <button ref={keep} type="button" className="cn-button" onClick={onCancel}>
            Keep it
          </button>
          <button type="button" className="cn-button ig-remove" disabled={busy} onClick={onRemove}>
            Remove {name}
          </button>
        </div>
      </div>
    </div>
  );
}
