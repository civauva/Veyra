import { useEffect, useId, useState } from "react";
import { Link, useSearchParams } from "react-router";

import type { AlertListItem, AlertQuery, AlertRule, AlertSummary } from "../api/types";
import { type ShellScene, useShell } from "../app/shellContext";
import { WeatherIcon } from "../home/WeatherIcon";
import { formatClock } from "../logs/format";
import { formatDuration } from "./alertFormat";
import { RuleIcon } from "./RuleIcon";
import { alertTitle, formatMeasure, formatThreshold, ruleMeasures, ruleName, ruleOrder } from "./ruleWords";
import { useAlertHistory } from "./useAlertHistory";
import { WeatherDays } from "./WeatherDays";
import "../home/home.css";
import "./alert.css";
import "./alerts.css";

/** The periods the screen offers, in days; resolved alerts are kept 90 days by default. */
const periods = [30, 90] as const;

/**
 * The Alerts screen: every alert of the last 30 or 90 days, newest first. A weather log shows the worst alert of each
 * day; filters narrow the list by state, rule, and service or host; the built-in rules and their thresholds stand
 * beside it. Rows lead to each alert's details. Filters live in the address, so a filtered list can be shared; the
 * environment is the one chosen in the top bar.
 */
export function AlertsScreen() {
  const [params, setParams] = useSearchParams();
  const { status } = useShell();
  // The environment comes from the top bar (VEY-23), the other filters from the address.
  const query = { ...readQuery(params), environment: status.environment };
  const history = useAlertHistory(query);
  const [text, setText] = useState(query.text);

  // Typing waits a moment before it filters, so every key does not start a request.
  useEffect(() => {
    if (text === query.text) {
      return;
    }

    const timer = window.setTimeout(() => change({ text }), 300);

    return () => window.clearTimeout(timer);
  });

  function change(next: Partial<AlertQuery>) {
    setParams(writeQuery({ ...query, ...next }), { replace: true });
  }

  function toggleRule(rule: AlertRule) {
    change({ rules: query.rules.includes(rule) ? query.rules.filter((one) => one !== rule) : [...query.rules, rule] });
  }

  return (
    <main className="home alert-screen alerts-screen">
      <div className="alert-page">
        <AlertsHero summary={history.summary} days={query.days} />
        {history.error && (
          <p className="home-stale" role="alert">
            Could not refresh: {history.error}
          </p>
        )}
        {history.summary && (
          <WeatherLog summary={history.summary} days={query.days} onDays={(days) => change({ days })} />
        )}

        <div className="alerts-split">
          <section className="glass alerts-list" aria-label="Alert list">
            <div className="alerts-filters">
              <div className="seg" role="group" aria-label="State">
                {(["open", "resolved", "all"] as const).map((state) => (
                  <button
                    key={state}
                    type="button"
                    aria-pressed={query.state === state}
                    onClick={() => change({ state })}
                  >
                    {state === "open" ? "Open" : state === "resolved" ? "Resolved" : "All"}
                    {history.summary && <em>{stateCount(history.summary, state)}</em>}
                  </button>
                ))}
              </div>
              <div className="rule-chips" role="group" aria-label="Rules">
                {ruleOrder.map((rule) => (
                  <button
                    key={rule}
                    type="button"
                    className="rule-chip"
                    aria-pressed={query.rules.includes(rule)}
                    onClick={() => toggleRule(rule)}
                  >
                    <RuleIcon rule={rule} size={14} />
                    {ruleName(rule)}
                    {history.summary && <em>{history.summary.rules.find((one) => one.rule === rule)?.count ?? 0}</em>}
                  </button>
                ))}
              </div>
              <label className="alerts-find">
                <span className="visually-hidden">Service, host, or disk</span>
                <input
                  type="search"
                  placeholder="Service, host, or disk"
                  value={text}
                  onChange={(event) => setText(event.target.value)}
                />
              </label>
            </div>

            <AlertRows items={history.items} now={history.summary ? new Date(history.summary.to) : null} />

            {history.hasMore && (
              <button
                type="button"
                className="step-button alerts-more"
                onClick={history.loadMore}
                disabled={history.loadingMore}
              >
                {history.loadingMore ? "Loading…" : "Show older alerts"}
              </button>
            )}
          </section>

          {history.summary && <BuiltInRules summary={history.summary} />}
        </div>
      </div>
    </main>
  );
}

/** The weather of the open alerts, the title, a sentence, and three counts. */
function AlertsHero({ summary, days }: { summary: AlertSummary | null; days: number }) {
  const scene = summary ? sceneOf(summary) : "clear";
  const cautions = summary ? summary.open - summary.openWarnings : 0;

  return (
    <section className="alerts-hero" aria-labelledby="alerts-title">
      <WeatherIcon kind={scene === "storm" ? "storm" : scene === "showers" ? "shower" : "sun"} size={88} />
      <div>
        <h1 id="alerts-title">Alerts</h1>
        <p className="say">{summary ? summarySentence(summary, days) : "Loading the alerts…"}</p>
      </div>
      {summary && (
        <dl className="alerts-stats">
          <div className="glass fact">
            <dt className="cap">Open</dt>
            <dd className="fact-value">{summary.open}</dd>
            <dd className="fact-note">
              {summary.open === 0 ? "none now" : `${summary.openWarnings} warning, ${cautions} caution`}
            </dd>
          </div>
          <div className="glass fact">
            <dt className="cap">{days} days</dt>
            <dd className="fact-value">{summary.total}</dd>
            <dd className="fact-note">
              {summary.warnings} {summary.warnings === 1 ? "warning" : "warnings"}
            </dd>
          </div>
          <div className="glass fact">
            <dt className="cap">Median</dt>
            <dd className="fact-value">
              {summary.medianSeconds === null ? "—" : formatDuration(summary.medianSeconds * 1000)}
            </dd>
            <dd className="fact-note">open to resolved</dd>
          </div>
        </dl>
      )}
    </section>
  );
}

/** Props for {@link WeatherLog}. */
interface WeatherLogProps {
  /** The counts of the period. */
  summary: AlertSummary;
  /** The period shown, in days. */
  days: number;
  /**
   * Called when the person picks another period.
   *
   * @param days - The new period.
   */
  onDays(days: number): void;
}

/** The day cells of the period, and the period to show. */
function WeatherLog({ summary, days, onDays }: WeatherLogProps) {
  const headingId = useId();

  return (
    <section className="glass weather-log" aria-labelledby={headingId}>
      <div className="weather-log-head">
        <h2 id={headingId} className="cap">
          Weather log · last {summary.days.length} days
        </h2>
        <div className="weather-log-tools">
          <ul className="log-legend" aria-hidden="true">
            <li className="legend-day-warning">warning</li>
            <li className="legend-day-caution">caution only</li>
            <li>number of alerts</li>
          </ul>
          <div className="seg" role="group" aria-label="Period">
            {periods.map((period) => (
              <button key={period} type="button" aria-pressed={days === period} onClick={() => onDays(period)}>
                {period} days
              </button>
            ))}
          </div>
        </div>
      </div>
      <WeatherDays days={summary.days} className={summary.days.length > 31 ? "long" : undefined} />
    </section>
  );
}

/** The alerts grouped by the day they opened, or a sentence when there are none. */
function AlertRows({ items, now }: { items: AlertListItem[] | null; now: Date | null }) {
  if (items === null) {
    return <p className="fine alerts-empty">Loading the alerts…</p>;
  }

  if (items.length === 0) {
    return <p className="fine alerts-empty">No alert matches these filters in this period.</p>;
  }

  const groups: { day: string; items: AlertListItem[] }[] = [];

  for (const item of items) {
    const day = item.openedAt.slice(0, 10);
    const group = groups.at(-1);

    if (group?.day === day) {
      group.items.push(item);
    } else {
      groups.push({ day, items: [item] });
    }
  }

  return (
    <div className="alerts-groups">
      {groups.map((group) => (
        <section key={group.day} aria-label={dayName(group.day, now)}>
          <h3 className="alerts-day cap">{dayName(group.day, now)}</h3>
          <ul className="alert-rows">
            {group.items.map((item) => (
              <li key={item.id}>
                <AlertRow item={item} now={now} />
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

/** One alert: its weather, what it says, its worst figure, and when it lasted on its day. */
function AlertRow({ item, now }: { item: AlertListItem; now: Date | null }) {
  const open = item.resolvedAt === null;
  const tone = item.worst === "Warning" ? "w" : "c";
  const end = item.resolvedAt ?? now?.toISOString() ?? item.updatedAt;
  const dayStart = Date.parse(`${item.openedAt.slice(0, 10)}T00:00:00Z`);
  const fromShare = Math.max(0, (Date.parse(item.openedAt) - dayStart) / 86_400_000);
  const toShare = Math.min(1, (Date.parse(end) - dayStart) / 86_400_000);
  const lasted = formatDuration(Date.parse(end) - Date.parse(item.openedAt));
  const shown = open ? item.value : item.peak;
  const worstNote = open
    ? item.rule === "disk-filling"
      ? "soonest"
      : item.peakAt
        ? `peak at ${formatClock(item.peakAt)}`
        : "now"
    : item.peakAt
      ? `${item.rule === "disk-filling" ? "soonest" : "peak"} at ${formatClock(item.peakAt)}`
      : "at its worst";

  return (
    <Link className={open ? `alert-line open ${tone}` : "alert-line"} to={`/alerts/${encodeURIComponent(item.id)}`}>
      <WeatherIcon kind={item.worst === "Warning" ? "storm" : "shower"} size={30} />
      <span className="alert-line-what">
        <b>{alertTitle(item.rule, item.block, shown, !open)}</b>
        <small>
          <span className="rule-tag">
            <RuleIcon rule={item.rule} size={12} />
            {ruleName(item.rule)}
          </span>
          <span className="kind-tag">{item.block.kind}</span>
          {item.environment ?? "no environment"}
        </small>
      </span>
      <span className="alert-line-peak">
        <b>{formatMeasure(item.rule, item.peak)}</b>
        <small>{worstNote}</small>
      </span>
      <span
        className="alert-line-span"
        aria-label={`${formatClock(item.openedAt)} to ${open ? "now" : formatClock(end)}, ${lasted}`}
      >
        <em>
          {formatClock(item.openedAt)} – {open ? "now" : formatClock(end)} · {lasted}
        </em>
        <i
          className={`${tone}${open ? " live" : ""}`}
          style={{ left: `${fromShare * 100}%`, width: `${Math.max(3, (toShare - fromShare) * 100)}%` }}
        />
      </span>
      <span className="alert-line-go" aria-hidden="true">
        ›
      </span>
    </Link>
  );
}

/** Every built-in rule with its thresholds and how many alerts it raised in the period. */
function BuiltInRules({ summary }: { summary: AlertSummary }) {
  return (
    <aside className="glass built-in-rules" aria-labelledby="built-in-rules">
      <h2 id="built-in-rules" className="cap">
        Built-in rules
      </h2>
      <ul>
        {summary.rules.map((rule) => (
          <li key={rule.rule} className={rule.enabled ? "built-in-rule" : "built-in-rule off"}>
            <span className="rule-icon" aria-hidden="true">
              <RuleIcon rule={rule.rule} size={18} />
            </span>
            <b>{ruleName(rule.rule)}</b>
            <span className="rule-count">
              {rule.count}
              <small>{summary.days.length} days</small>
            </span>
            <p>
              {rule.enabled ? (
                <>
                  {ruleMeasures(rule.rule, rule.windowSeconds)}:{" "}
                  <span className="caution">caution {formatThreshold(rule.rule, rule.cautionThreshold)}</span>,{" "}
                  <span className="warning">warning {formatThreshold(rule.rule, rule.warningThreshold)}</span>.
                </>
              ) : (
                "Switched off for this installation."
              )}
            </p>
          </li>
        ))}
      </ul>
      <p className="fine">
        Thresholds are settings of the installation (<code>Veyra:Alerting</code>). Resolved alerts are kept 90 days by
        default.
      </p>
    </aside>
  );
}

/** The scene behind the screen: storm with an open warning, showers with open cautions only, clear without. */
function sceneOf(summary: AlertSummary): ShellScene {
  return summary.openWarnings > 0 ? "storm" : summary.open > 0 ? "showers" : "clear";
}

/** The sentence under the title. */
function summarySentence(summary: AlertSummary, days: number): string {
  const open =
    summary.open === 0
      ? "No alert is open."
      : summary.open === 1
        ? "One alert is open."
        : `${summary.open} alerts are open.`;
  const period =
    summary.total === 0
      ? ` No alerts in the last ${days} days.`
      : ` ${summary.total} ${summary.total === 1 ? "alert" : "alerts"} in the last ${days} days` +
        (summary.medianSeconds === null
          ? "."
          : `; the median cleared in ${formatDuration(summary.medianSeconds * 1000)}.`);

  return open + period;
}

/** How many alerts a state filter holds over the period. */
function stateCount(summary: AlertSummary, state: AlertQuery["state"]): number {
  return state === "open" ? summary.open : state === "resolved" ? summary.resolved : summary.total;
}

/** A day heading: `Today`, `Yesterday`, or `Nov 8`. */
function dayName(day: string, now: Date | null): string {
  const today = (now ?? new Date()).toISOString().slice(0, 10);
  const yesterday = new Date(Date.parse(`${today}T00:00:00Z`) - 86_400_000).toISOString().slice(0, 10);

  if (day === today) {
    return "Today";
  }

  if (day === yesterday) {
    return "Yesterday";
  }

  return new Date(`${day}T00:00:00Z`).toLocaleDateString("en-US", { month: "short", day: "numeric", timeZone: "UTC" });
}

/** The filters from the address; anything unknown falls back to the default. */
function readQuery(params: URLSearchParams): Omit<AlertQuery, "environment"> {
  const days = Number(params.get("days"));
  const state = params.get("state");

  return {
    days: periods.includes(days as (typeof periods)[number]) ? days : 30,
    state: state === "open" || state === "resolved" ? state : "all",
    rules: params.getAll("rule").filter((rule): rule is AlertRule => (ruleOrder as string[]).includes(rule)),
    text: params.get("q") ?? "",
  };
}

/** The filters as address parameters, leaving out the defaults. */
function writeQuery(query: Omit<AlertQuery, "environment">): URLSearchParams {
  const params = new URLSearchParams();

  if (query.days !== 30) {
    params.set("days", String(query.days));
  }

  if (query.state !== "all") {
    params.set("state", query.state);
  }

  for (const rule of query.rules) {
    params.append("rule", rule);
  }

  if (query.text) {
    params.set("q", query.text);
  }

  return params;
}
