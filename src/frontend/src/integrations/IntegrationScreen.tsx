import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";

import { formatClockTime } from "../metrics/metricFormat";
import { serverStateWords } from "../settings/monitoringFormat";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import {
  authenticationShort,
  configWords,
  elapsedWords,
  historyWords,
  intervalWords,
  probeFacts,
  probeFailureWords,
  readerWords,
  runTimeWords,
  uptimeWords,
} from "./integrationWords";
import { useIntegration } from "./useIntegrations";
import "./integrations.css";

/**
 * One integration's package page (ADR 0028) as every look shows it by default: the template and its version with
 * Update and Roll back, the Collector, validation with Validate now, each module's figures, the checks, the next
 * steps, and the history. The Console look draws its own after reference screenshot 4.
 */
export function IntegrationScreen() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const view = useIntegration(id);
  const [removing, setRemoving] = useState(false);
  const trail = (label: string) => [{ label: "Integrations", to: "/integrations" }, { label }];

  if (view.notFound) {
    return (
      <main className="ints">
        <ScreenBar trail={trail("Not found")} />
        <p>Veyra monitors no such SQL Server. It may have been removed.</p>
      </main>
    );
  }

  if (!view.details) {
    return (
      <main className="ints">
        <ScreenBar trail={trail("…")} />
        <p>{view.error ? `Could not read the integration: ${view.error}` : "Reading the integration…"}</p>
      </main>
    );
  }

  const { integration, collector, checks, nextSteps, history } = view.details;
  const pack = view.details.package;
  const state = serverStateWords(integration.state);
  const config = configWords(collector.configState);
  const probe = view.validation.probe ?? view.details.validation.lastProbe;

  async function remove() {
    if (await view.remove()) {
      void navigate("/integrations");
    }
  }

  return (
    <main className="ints">
      <ScreenBar trail={trail(integration.name)} />
      <h1>
        {pack.name} {pack.version}
      </h1>
      <p className="ints-lead">
        {integration.name} · {state.label} · read by {readerWords(collector.agentName)} · {integration.environment}
      </p>
      {(view.error || view.actionError) && (
        <p className="ints-error" role="alert">
          {view.actionError ?? `Could not refresh: ${view.error}`}
        </p>
      )}

      <section className="ints-section" aria-labelledby="ints-package">
        <h2 id="ints-package">
          Package
          <HelpTip label="Package">
            The template version this server is read with, when and by whom it was set, the Collector versions it works
            with, the least SQL Server it supports, and the login. Update moves it to the current version, Roll back to
            the one before.
          </HelpTip>
        </h2>
        <dl className="ints-facts">
          <div>
            <dt>Version</dt>
            <dd>
              {pack.version}
              {pack.previousVersion ? ` (previous ${pack.previousVersion})` : ""}, set {formatClockTime(pack.setAt)}
              {pack.setBy ? ` by ${pack.setBy}` : ""}
            </dd>
          </div>
          <div>
            <dt>Collector</dt>
            <dd>
              {collector.healthy ? "Healthy" : "Unhealthy"} · {pack.collectorVersion ?? "version not known"} · up{" "}
              {uptimeWords(collector.uptimeSeconds)} · needs {pack.collectorCompatibility}
            </dd>
          </div>
          <div>
            <dt>Supports</dt>
            <dd>{pack.minimumSqlServer} and later</dd>
          </div>
          <div>
            <dt>Login</dt>
            <dd>
              {pack.login} ({authenticationShort[pack.authentication]}) ·{" "}
              {pack.loginLacks.length === 0 ? "nothing missing" : `lacks ${pack.loginLacks.join(", ")}`}
            </dd>
          </div>
        </dl>
        <div className="ints-actions">
          <button
            type="button"
            className="button small"
            disabled={view.busy || pack.version === pack.latestVersion}
            onClick={() => void view.setVersion(pack.latestVersion)}
          >
            Update to {pack.latestVersion}
          </button>
          {pack.previousVersion && pack.previousVersion !== pack.version && (
            <button
              type="button"
              className="button small"
              disabled={view.busy}
              onClick={() => pack.previousVersion && void view.setVersion(pack.previousVersion)}
            >
              Roll back to {pack.previousVersion}
            </button>
          )}
          {removing ? (
            <>
              <button type="button" className="button small danger" disabled={view.busy} onClick={() => void remove()}>
                Remove {integration.name}
              </button>
              <button type="button" className="button small quiet" onClick={() => setRemoving(false)}>
                Keep
              </button>
            </>
          ) : (
            <button type="button" className="button small quiet" onClick={() => setRemoving(true)}>
              Remove integration
            </button>
          )}
        </div>
      </section>

      <section className="ints-section" aria-labelledby="ints-validation">
        <h2 id="ints-validation">
          Configuration validation
          <HelpTip label="Configuration validation">
            Whether the agent applied the configuration Veyra sent, and the last test of the login and its permissions.
            Validate now runs that test on the agent; its Collector restarts twice for a few seconds.
          </HelpTip>
        </h2>
        <p>
          Configuration {config.title.toLowerCase()}
          {collector.configError ? `: ${collector.configError}` : ""}.{" "}
          {view.validation.running
            ? `Validating… ${elapsedWords(view.validation.elapsedSeconds)}`
            : probe?.result
              ? `Last check ${formatClockTime(probe.finishedAt ?? probe.startedAt)}: ${probeFacts(probe.result)}.`
              : probe?.error
                ? `Last check failed: ${probeFailureWords(probe.error.reason).title}. ${probe.error.message}`
                : "Not validated yet."}
        </p>
        <button
          type="button"
          className="button small"
          disabled={view.validation.running}
          onClick={() => view.validate()}
        >
          Validate now
        </button>
      </section>

      <section className="ints-section" aria-labelledby="ints-modules">
        <h2 id="ints-modules">
          Modules
          <HelpTip label="Modules">
            The template&apos;s reads in groups, each with its interval, and the metric series and points each wrote in
            the time range. Switched-off modules are not read.
          </HelpTip>
        </h2>
        <ul className="ints-list">
          {view.details.modules.map((module) => (
            <li key={module.id}>
              <b>{module.name}</b>
              <small>
                {module.enabled
                  ? `every ${intervalWords(module.intervalSeconds)} · ${module.series} series · ${module.points.toLocaleString("en")} points`
                  : "off"}
              </small>
            </li>
          ))}
        </ul>
      </section>

      <section className="ints-section" aria-labelledby="ints-checks">
        <h2 id="ints-checks">
          Active checks
          <HelpTip label="Active checks">
            The time SQL Server spends on each run of the integration&apos;s reads, from its statement statistics.
          </HelpTip>
        </h2>
        <ul className="ints-list">
          {checks.map((check) => (
            <li key={check.name} className={check.ok ? undefined : "bad"}>
              <b>{check.name}</b>
              <small>
                {check.module} · {runTimeWords(check.secondsPerRun)} per run · {check.runs.toLocaleString("en")} runs
              </small>
            </li>
          ))}
        </ul>
      </section>

      <section className="ints-section" aria-labelledby="ints-next">
        <h2 id="ints-next">
          Next steps
          <HelpTip label="Next steps">What the installation still lacks, ticked off by the server once done.</HelpTip>
        </h2>
        <ul className="ints-list">
          {nextSteps.map((step) => (
            <li key={step.id} className={step.done ? "done" : undefined}>
              {step.link && !step.done ? <Link to={step.link}>{step.text}</Link> : <span>{step.text}</span>}
              <small>{step.done ? "done" : "to do"}</small>
            </li>
          ))}
        </ul>
      </section>

      <section className="ints-section" aria-labelledby="ints-history">
        <h2 id="ints-history">
          History
          <HelpTip label="History">
            Changes of the template version, the modules, and the connection, newest first.
          </HelpTip>
        </h2>
        <ul className="ints-list">
          {history.map((item, index) => (
            <li key={`${item.at}-${index}`}>
              <span>{historyWords(item)}</span>
              <small>
                {formatClockTime(item.at)} · {item.by ?? "Veyra"}
              </small>
            </li>
          ))}
        </ul>
      </section>
    </main>
  );
}
