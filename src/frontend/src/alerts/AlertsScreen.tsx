import { type ReactNode, useEffect, useId, useState } from "react";
import { Link, useSearchParams } from "react-router";

import type { AlertListItem, AlertQuery, AlertRule, AlertSummary } from "../api/types";
import { HelpTip } from "../ui/HelpTip";
import { ScreenBar } from "../ui/ScreenBar";
import { type ShellScene, useShell } from "../app/shellContext";
import { WeatherIcon } from "../home/WeatherIcon";
import { useSession } from "../session/useSession";
import { formatClock } from "../logs/format";
import { formatDuration } from "./alertFormat";
import { RuleIcon } from "./RuleIcon";
import { alertTitle, formatMeasure, formatThreshold, ruleMeasures, ruleName, ruleOrder } from "./ruleWords";
import { useAlertHistory } from "./useAlertHistory";
import { WeatherDays } from "./WeatherDays";
import { instantOfLocalDay, localDay, localDayName } from "../time/zone";
import "../home/home.css";
import "./alert.css";
import "./alerts.css";
import { useViewMode } from "../ui/ViewToggle";
import { periods, readQuery, stateCount, summarySentence, writeQuery } from "./alertQuery";

/**
 * The Alerts screen: every alert of the last 30 or 90 days, newest first. A weather log shows the worst alert of each
 * day; filters narrow the list by state, rule, and service or host; the built-in rules and their thresholds stand
 * beside it. Rows lead to each alert's details. Filters live in the address, so a filtered list can be shared; the
 * environment is the one chosen in the top bar.
 */
export function AlertsScreen() {
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
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
      <ScreenBar trail={[{ label: "Alerts" }]} view />
      <div className="alert-page">
        <AlertsHero summary={history.summary} days={query.days} />
        {history.error && (
          <p className="home-stale" role="alert">
            Could not refresh: {history.error}
          </p>
        )}
        {advanced && history.summary && (
          <WeatherLog summary={history.summary} days={query.days} onDays={(days) => change({ days })} />
        )}

        <div className={advanced ? "alerts-split" : "alerts-split alone"}>
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
              {(advanced || query.rules.length > 0) && (
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
              )}
              {(advanced || text) && (
                <label className="alerts-find">
                  <span className="visually-hidden">Service, host, or disk</span>
                  <input
                    type="search"
                    placeholder="Service, host, or disk"
                    value={text}
                    onChange={(event) => setText(event.target.value)}
                  />
                </label>
              )}
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

          {advanced && history.summary && <BuiltInRules summary={history.summary} />}
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
            <StatName name="Open">
              Alerts open now, and how many of them are warnings and how many cautions. Each resolves by itself once its
              measure clears.
            </StatName>
            <dd className="fact-value">{summary.open}</dd>
            <dd className="fact-note">
              {summary.open === 0 ? "none now" : `${summary.openWarnings} warning, ${cautions} caution`}
            </dd>
          </div>
          <div className="glass fact">
            <StatName name={`${days} days`}>
              Every alert that was open at some time in the last {days} days, and how many of them reached warning.
            </StatName>
            <dd className="fact-value">{summary.total}</dd>
            <dd className="fact-note">
              {summary.warnings} {summary.warnings === 1 ? "warning" : "warnings"}
            </dd>
          </div>
          <div className="glass fact">
            <StatName name="Median">
              How long alerts took from opening to resolving: half of those that resolved in the last {days} days
              cleared faster than this, half took longer.
            </StatName>
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

/**
 * The name of one of the hero's counts, with the question mark that explains it (VEY-60), as on the alert's facts.
 *
 * @param props - The name, and what the count means.
 */
function StatName({ name, children }: { name: string; children: ReactNode }) {
  return (
    <dt className="fact-name">
      <span className="cap">{name}</span>
      <HelpTip label={name}>{children}</HelpTip>
    </dt>
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
        <div className="alert-name">
          <h2 id={headingId} className="cap">
            Weather log · last {summary.days.length} days
          </h2>
          <HelpTip label="Weather log">
            One cell per day with the worst alert that opened that day and how many opened. Alerts opened before the
            period count on its first day. Days start at midnight in your time zone.
          </HelpTip>
        </div>
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
    const day = localDay(item.openedAt);
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
  const dayStart = instantOfLocalDay(localDay(item.openedAt));
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
  const { state } = useSession();
  const admin = state.status === "signedIn" && state.session.role === "Admin";

  return (
    <aside className="glass built-in-rules" aria-labelledby="built-in-rules">
      <div className="alert-name">
        <h2 id="built-in-rules" className="cap">
          Built-in rules
        </h2>
        <HelpTip label="Built-in rules">
          The rules Veyra checks every 30 seconds, with their caution and warning thresholds as this installation sets
          them, and how many alerts each raised in the period.
        </HelpTip>
      </div>
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
        Thresholds and how long resolved alerts are kept are settings of the installation, which Admins change.{" "}
        {admin && <Link to="/settings?section=Rules">Change them in Settings</Link>}
      </p>
    </aside>
  );
}

/** The scene behind the screen: storm with an open warning, showers with open cautions only, clear without. */
function sceneOf(summary: AlertSummary): ShellScene {
  return summary.openWarnings > 0 ? "storm" : summary.open > 0 ? "showers" : "clear";
}

/** A day heading: `Today`, `Yesterday`, or `Nov 8`, on the person's calendar (VEY-22). */
function dayName(day: string, now: Date | null): string {
  return localDayName(day, now ?? new Date());
}
