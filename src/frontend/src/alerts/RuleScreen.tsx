import { useEffect, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { AlertListItem, AlertRule, AlertSummary, BlockKind } from "../api/types";
import { useShell } from "../app/shellContext";
import { useSession } from "../session/useSession";
import { CommandBlock } from "../ui/CommandBlock";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import { useHashPulse } from "../ui/useHashPulse";
import { ruleGuidance } from "./ruleGuidance";
import { alertFixPath } from "./rulePath";
import { formatThreshold, ruleMeasures, ruleName, ruleOrder, subjectLabel, titleOnScreen } from "./ruleWords";
import "./rule.css";

/** The block kinds an address may name. */
const kinds: BlockKind[] = ["service", "host", "database", "container"];

/**
 * A rule and how to fix what it finds (VEY-83): the page a warning leads to when it has no alert of its own to open,
 * such as a late backup in a list. It says what the rule measures and where its thresholds stand, what a finding
 * means and the steps to fix it, named for the subject the address gives, and the subject's open alerts of the rule,
 * each a click from its own page. The address may point at `#fix`, which scrolls to the steps and pulses them.
 */
export function RuleScreen() {
  const { rule: named = "" } = useParams();
  const [params] = useSearchParams();
  const { status } = useShell();
  const rule = (ruleOrder as string[]).includes(named) ? (named as AlertRule) : null;
  const kind = kinds.find((one) => one === params.get("kind")) ?? null;
  const name = params.get("name");
  const part = params.get("part");
  const subject = kind && name ? { kind, name, part, id: "" } : null;
  const [summary, setSummary] = useState<AlertSummary | null>(null);
  const [open, setOpen] = useState<AlertListItem[] | null>(null);
  const fix = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!rule) {
      return undefined;
    }

    const controller = new AbortController();

    api
      .alertSummary(30, status.environment, controller.signal)
      .then(setSummary)
      .catch(() => undefined);
    api
      .alerts(
        {
          days: 30,
          state: "open",
          rules: [rule],
          text: "",
          environment: status.environment,
          ...(subject ? { kind: subject.kind, block: subject.name } : {}),
          ...(subject?.part ? { part: subject.part } : {}),
          order: "worst",
          limit: 20,
        },
        null,
        controller.signal,
      )
      .then((page) => setOpen(page.items))
      .catch(() => setOpen([]));

    return () => controller.abort();
    // The subject's words make the request; the object is new on each render.
  }, [rule, status.environment, subject?.kind, subject?.name, subject?.part]); // eslint-disable-line react-hooks/exhaustive-deps

  const guidance = rule
    ? ruleGuidance({ rule, block: subject ?? { kind: "service", name: "it", part: null, id: "" } })
    : null;
  useHashPulse("fix", fix, guidance !== null);

  if (!rule) {
    return (
      <main className="ru">
        <ScreenBar trail={[{ label: "Alerts", to: "/alerts" }, { label: "Rule" }]} />
        <p className="ru-empty">Veyra has no rule by that name.</p>
      </main>
    );
  }

  const thresholds = summary?.rules.find((one) => one.rule === rule) ?? null;

  return (
    <main className="ru">
      <ScreenBar trail={[{ label: "Alerts", to: "/alerts" }, { label: "Rules" }, { label: ruleName(rule) }]} />
      <header className="ru-head">
        <h1>{ruleName(rule)}</h1>
        <p>
          {subject ? <>About {subjectLabel(subject)}. </> : null}
          {thresholds ? ruleMeasures(rule, thresholds.windowSeconds) : null}
        </p>
        {thresholds && (
          <dl className="ru-thresholds">
            <div>
              <dt>Caution at</dt>
              <dd className="ru-caution">
                {thresholds.enabled ? formatThreshold(rule, thresholds.cautionThreshold) : "off"}
              </dd>
            </div>
            <div>
              <dt>Warning at</dt>
              <dd className="ru-warning">
                {thresholds.enabled ? formatThreshold(rule, thresholds.warningThreshold) : "off"}
              </dd>
            </div>
            <div>
              <dt>Alerts in 30 days</dt>
              <dd>{thresholds.count}</dd>
            </div>
          </dl>
        )}
      </header>

      {guidance ? (
        <div className="ru-guide">
          <section className="ru-card" aria-labelledby="ru-means">
            <h2 id="ru-means">What it means</h2>
            <p>{guidance.meaning}</p>
          </section>
          <section ref={fix} id="fix" className="ru-card" aria-labelledby="ru-fix" tabIndex={-1}>
            <h2 id="ru-fix">
              How to fix
              <HelpTip label="How to fix">
                The steps to take, in order, with scripts ready to copy. Veyra never runs them. Once the measure is back
                under the threshold, an open alert resolves by itself.
              </HelpTip>
            </h2>
            <ol className="ru-steps">
              {guidance.steps.map((step) => (
                <li key={step.text}>
                  {step.text}
                  {step.script && <CommandBlock label={step.text} text={step.script} />}
                </li>
              ))}
            </ol>
          </section>
        </div>
      ) : (
        <p className="ru-empty">Reading what this rule means…</p>
      )}

      <section className="ru-card" aria-labelledby="ru-open">
        <h2 id="ru-open">{subject ? `Open alerts of ${subjectLabel(subject)}` : "Open alerts of this rule"}</h2>
        {open === null ? (
          <p className="ru-quiet">Reading the open alerts…</p>
        ) : open.length === 0 ? (
          <p className="ru-quiet">
            None is open now: the measure is under the threshold, or the rule has not checked yet.
          </p>
        ) : (
          <ul className="ru-alerts">
            {open.map((alert) => (
              <li key={alert.id} className={alert.severity === "Warning" ? "bad" : "warn"}>
                <Link to={alertFixPath(alert.id)}>{titleOnScreen(alert)}</Link>
                <span>{alert.severity === "Warning" ? "warning" : "caution"}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <RulesLink />
    </main>
  );
}

/** For Admins, where the thresholds are changed. */
function RulesLink() {
  const { state } = useSession();

  return state.status === "signedIn" && state.session.role === "Admin" ? (
    <p className="ru-quiet">
      <Link to="/settings?section=Rules">Change the thresholds in Settings → Alert rules</Link>
    </p>
  ) : null;
}
