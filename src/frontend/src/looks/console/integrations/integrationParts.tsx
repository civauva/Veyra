import { useState } from "react";

import type { IntegrationModule, IntegrationModuleInfo, Probe } from "../../../api/types";
import { elapsedWords, intervalWords, probeFacts, probeFailureWords } from "../../../integrations/integrationWords";
import { changeModule } from "../../../integrations/wizard";
import { ConsoleIcon } from "../ConsoleIcon";

/*
 * The parts Console's integration screens share (VEY-78, ADR 0028): the module rows with their switch and interval,
 * the answer of a probe, and a script to copy. They draw what the feature hooks hold and work nothing out.
 */

/** Props for {@link ModuleRows}. */
export interface ModuleRowsProps {
  /** The template's modules, in the catalog's order. */
  catalog: IntegrationModuleInfo[];
  /** Each module's switch and interval. */
  modules: IntegrationModule[];
  /** Called with the modules after a change. */
  onChange(modules: IntegrationModule[]): void;
  /** Whether the rows are read-only, as while saving. */
  disabled?: boolean;
}

/**
 * One row per module, as the VEY-78 mockup: a tick to switch it on, its name and what it reads, and how often. A
 * required module, such as Core, stays ticked and says it is always on.
 */
export function ModuleRows({ catalog, modules, onChange, disabled = false }: ModuleRowsProps) {
  return (
    <ul className="ig-modules">
      {catalog.map((info) => {
        const module = modules.find((one) => one.id === info.id) ?? {
          id: info.id,
          enabled: false,
          intervalSeconds: info.defaultIntervalSeconds,
        };
        const descriptionId = `ig-module-${info.id}`;

        return (
          <li key={info.id} className={module.enabled ? "ig-module on" : "ig-module"}>
            <input
              type="checkbox"
              className="ig-check"
              aria-label={info.name}
              aria-describedby={descriptionId}
              checked={module.enabled}
              disabled={disabled || info.required}
              onChange={(event) => onChange(changeModule(modules, catalog, info.id, { enabled: event.target.checked }))}
            />
            <div className="ig-module-text">
              <b>
                {info.name}
                {info.required && <small className="ig-required"> always on</small>}
              </b>
              <small id={descriptionId}>{info.description}</small>
            </div>
            <select
              className="ig-select ig-interval"
              aria-label={`${info.name}: how often`}
              value={module.intervalSeconds}
              disabled={disabled || !module.enabled}
              onChange={(event) =>
                onChange(changeModule(modules, catalog, info.id, { intervalSeconds: Number(event.target.value) }))
              }
            >
              {info.choices.map((seconds) => (
                <option key={seconds} value={seconds}>
                  every {intervalWords(seconds)}
                </option>
              ))}
            </select>
          </li>
        );
      })}
    </ul>
  );
}

/** Props for {@link ProbeBanner}. */
export interface ProbeBannerProps {
  /** The probe, or null before one starts. */
  probe: Probe | null;
  /** Whether it is pending. */
  running: boolean;
  /** Seconds since it started. */
  elapsedSeconds: number;
  /** The agent that runs it, as a sentence names it. */
  agent: string;
  /** Why it could not be started or followed, or null. */
  error: string | null;
}

/**
 * How a probe stands, as the mockup's banner: testing with the time it has taken, the connection's facts when it
 * succeeded, or why it failed with what to do.
 */
export function ProbeBanner({ probe, running, elapsedSeconds, agent, error }: ProbeBannerProps) {
  if (running) {
    return (
      <div className="ig-banner wait" role="status">
        <span className="ig-spinner" aria-hidden="true" />
        <div>
          <b>Testing through {agent}…</b>
          <small>
            {elapsedWords(elapsedSeconds)} so far. A test takes 30 to 60 seconds; Veyra waits up to 2 minutes.
          </small>
        </div>
      </div>
    );
  }

  if (probe?.state === "succeeded" && probe.result) {
    return (
      <div className="ig-banner ok" role="status">
        <span className="ig-banner-icon" aria-hidden="true">
          <ConsoleIcon name="check" size={14} />
        </span>
        <div>
          <b>Connection successful</b>
          <small>{probeFacts(probe.result)}</small>
        </div>
      </div>
    );
  }

  if (probe?.state === "failed" && probe.error) {
    const words = probeFailureWords(probe.error.reason);

    return (
      <div className="ig-banner bad" role="alert">
        <span className="ig-banner-icon" aria-hidden="true">
          <ConsoleIcon name="close" size={14} />
        </span>
        <div>
          <b>{words.title}</b>
          <small>{words.fix}</small>
          {probe.error.message && <small className="mono">{probe.error.message}</small>}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="ig-banner bad" role="alert">
        <span className="ig-banner-icon" aria-hidden="true">
          <ConsoleIcon name="close" size={14} />
        </span>
        <div>
          <b>The test could not start</b>
          <small>{error}</small>
        </div>
      </div>
    );
  }

  return null;
}

/** Props for {@link ScriptBlock}. */
export interface ScriptBlockProps {
  /** What the script does, naming the block and its Copy button for screen readers. */
  label: string;
  /** The script. */
  text: string;
  /** A secret in it, shown as dots until the person asks to see it; copying always takes the real script. */
  secret?: string;
}

/** A script to run in SQL Server Management Studio, with Copy, and the password hidden until shown. */
export function ScriptBlock({ label, text, secret }: ScriptBlockProps) {
  const [copied, setCopied] = useState(false);
  const [shown, setShown] = useState(false);
  const hidden = secret ? secret.replaceAll("'", "''") : "";
  const visible = !secret || shown ? text : text.replaceAll(hidden, "•".repeat(12));

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2_000);
    } catch {
      setCopied(false);
    }
  }

  return (
    <div className="ig-script">
      <div className="ig-script-tools">
        {secret && (
          <button type="button" className="link-button" aria-pressed={shown} onClick={() => setShown(!shown)}>
            {shown ? "Hide the password" : "Show the password"}
          </button>
        )}
        <button type="button" className="link-button" aria-label={`Copy: ${label}`} onClick={() => void copy()}>
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
      <pre aria-label={label}>{visible}</pre>
    </div>
  );
}

/** Props for {@link BigState}. */
export interface BigStateProps {
  /** Its tone: green, red, or waiting. */
  tone: "ok" | "bad" | "wait";
  /** The headline, such as "Installed". */
  title: string;
  /** The line under it. */
  detail: string;
}

/** A panel's state in large type with its mark, as the package page's top row. */
export function BigState({ tone, title, detail }: BigStateProps) {
  return (
    <div className={`ig-big ${tone}`}>
      <span className="ig-big-mark" aria-hidden="true">
        <ConsoleIcon name={tone === "ok" ? "check" : tone === "bad" ? "close" : "refresh"} size={15} />
      </span>
      <div>
        <b>{title}</b>
        <small>{detail}</small>
      </div>
    </div>
  );
}
