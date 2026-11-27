import { useEffect, useState, type ReactNode } from "react";
import { Link, useSearchParams } from "react-router";

import { formatDuration } from "../../alerts/alertFormat";
import { alertFixPath, rulePath } from "../../alerts/rulePath";
import { daysOfWindow, readQuery, stateCount, summarySentence, writeQuery } from "../../alerts/alertQuery";
import { alertTitle, formatMeasure, formatThreshold, ruleMeasures, ruleName, ruleOrder } from "../../alerts/ruleWords";
import { useAlertHistory } from "../../alerts/useAlertHistory";
import type { AlertListItem, AlertQuery, AlertRule, AlertSummary } from "../../api/types";
import { useShell } from "../../app/shellContext";
import { timeSearch } from "../../app/time";
import { formatClock } from "../../logs/format";
import { StackedBars } from "../../overview/StackedBars";
import { localDay, localDayName } from "../../time/zone";
import { HelpTip } from "../../ui/HelpTip";
import { ScreenBar } from "../../ui/ScreenBar";
import { useViewMode } from "../../ui/ViewToggle";

/**
 * Console's Alerts screen (VEY-69): the same alerts and filters as every look (`alerts/alertQuery.ts`,
 * `useAlertHistory`), drawn as an operations console instead of a weather report. Figures for the period, the alerts
 * of each day as bars, the list as a table worst first within each day, and the built-in rules with their thresholds.
 * The period is the top bar's range in whole days, at least one, so the screen has no period choice of its own.
 */
export function ConsoleAlerts() {
  const [viewMode] = useViewMode();
  const [params, setParams] = useSearchParams();
  const { status, time } = useShell();
  const filters = readQuery(params);
  const query = {
    ...filters,
    days: time ? daysOfWindow(time.window()) : filters.days,
    environment: status.environment,
  };
  const history = useAlertHistory(query);
  const [text, setText] = useState(query.text);
  const summary = history.summary;

  // Typing waits a moment before it filters, so every key does not start a request.
  useEffect(() => {
    if (text === query.text) {
      return undefined;
    }

    const timer = window.setTimeout(() => change({ text }), 300);

    return () => window.clearTimeout(timer);
  });

  // The filters change; the top bar's range stays in the address as it is, and the days follow from it.
  function change(next: Partial<AlertQuery>) {
    const written = writeQuery({ ...query, ...next, days: filters.days });

    for (const [key, value] of new URLSearchParams(time ? timeSearch(time.choice) : "")) {
      written.set(key, value);
    }

    setParams(written, { replace: true });
  }

  return (
    <main className="cn-screen" data-view={viewMode}>
      <ScreenBar trail={[{ label: "Alerts" }]} view />
      <header className="cn-head">
        <div>
          <h1>
            Alerts
            {summary && summary.open > 0 && (
              <span className={summary.openWarnings > 0 ? "cn-pill bad" : "cn-pill warn"}>{summary.open} open</span>
            )}
          </h1>
          <p>{summary ? summarySentence(summary, query.days) : "Reading the alerts…"}</p>
        </div>
      </header>

      {history.error && (
        <p className="cn-error" role="alert">
          Could not refresh: {history.error}
        </p>
      )}

      {summary && <Figures summary={summary} days={query.days} onState={(state) => change({ state })} />}

      <div className="cn-columns">
        <section className="cn-card" aria-labelledby="cn-alert-list">
          <header>
            <h2 id="cn-alert-list">Alert list</h2>
            <HelpTip label="Alert list">
              Every alert of the period, newest day first and warnings first within a day. Open one for what it means,
              how to fix it, its chart, and its timeline. The server filters the list.
            </HelpTip>
          </header>
          <div className="cn-filters">
            <div className="cn-seg" role="group" aria-label="State">
              {(["open", "resolved", "all"] as const).map((state) => (
                <button
                  key={state}
                  type="button"
                  aria-pressed={query.state === state}
                  onClick={() => change({ state })}
                >
                  {state === "open" ? "Open" : state === "resolved" ? "Resolved" : "All"}
                  {summary && <em>{stateCount(summary, state)}</em>}
                </button>
              ))}
            </div>
            <label className="cn-filter">
              <span>Rule:</span>
              <select
                value={query.rules[0] ?? ""}
                onChange={(event) => change({ rules: event.target.value ? [event.target.value as AlertRule] : [] })}
              >
                <option value="">Any</option>
                {ruleOrder.map((rule) => (
                  <option key={rule} value={rule}>
                    {ruleName(rule)}
                    {summary ? ` (${summary.rules.find((one) => one.rule === rule)?.count ?? 0})` : ""}
                  </option>
                ))}
              </select>
            </label>
            <input
              type="search"
              className="cn-find"
              aria-label="Service, host, or disk"
              placeholder="Service, host, or disk"
              value={text}
              onChange={(event) => setText(event.target.value)}
            />
          </div>
          <AlertTable items={history.items} now={summary ? new Date(summary.to) : null} />
          {history.hasMore && (
            <footer>
              <button type="button" className="link-button" onClick={history.loadMore} disabled={history.loadingMore}>
                {history.loadingMore ? "Loading…" : "Show older alerts"}
              </button>
            </footer>
          )}
        </section>
        {summary && <Rules summary={summary} />}
      </div>
    </main>
  );
}

/** The figures of the period and the alerts of each day. */
function Figures({
  summary,
  days,
  onState,
}: {
  summary: AlertSummary;
  days: number;
  onState(state: "open" | "resolved" | "all"): void;
}) {
  const cautions = summary.open - summary.openWarnings;
  const label = (date: string | undefined) => (date ? date.slice(5).replace("-", "/") : "");

  return (
    // One compact band (VEY-83): the four figures beside the alerts of each day, as tall as their content.
    <div className="cn-card cn-band">
      <section className="cn-band-figures" aria-label="Figures">
        <Figure
          label="Open now"
          onOpen={() => onState("open")}
          value={summary.open}
          detail={`${summary.openWarnings} warning · ${cautions} caution`}
          tone={summary.openWarnings > 0 ? "bad" : summary.open > 0 ? "warn" : undefined}
          help="Alerts open now, by severity. Red is warning and amber is caution on every screen."
        />
        <Figure
          label={days === 1 ? "Last day" : `Last ${days} days`}
          onOpen={() => onState("all")}
          value={summary.total}
          detail={`${summary.warnings} reached warning`}
          help="Every alert open at some time in the period, and how many of them reached warning."
        />
        <Figure
          label="Resolved"
          onOpen={() => onState("resolved")}
          value={summary.resolved}
          detail="closed by their rules"
          help="Alerts of the period that closed: their rule saw the measure back under its threshold."
        />
        <Figure
          label="Median time open"
          onOpen={() => onState("resolved")}
          value={summary.medianSeconds === null ? "—" : formatDuration(summary.medianSeconds * 1000)}
          detail="from open to resolved"
          help="Half of the resolved alerts closed faster than this."
        />
      </section>
      <section className="cn-band-chart" aria-labelledby="cn-alert-days">
        <header>
          <h2 id="cn-alert-days">Alerts per day</h2>
          <HelpTip label="Alerts per day">
            How many alerts were open on each day of the period, in red when one of them reached warning and in amber
            when only cautions were.
          </HelpTip>
        </header>
        <StackedBars
          title={days === 1 ? "Alerts per day, last day" : `Alerts per day, last ${days} days`}
          steps={summary.days.map((day) => label(day.date))}
          labels={[
            label(summary.days[0]?.date),
            label(summary.days[Math.floor(summary.days.length / 2)]?.date),
            "today",
          ]}
          layers={[
            {
              name: "Days with a warning",
              color: "var(--error-fg)",
              values: summary.days.map((day) => (day.worst === "Warning" ? day.count : 0)),
            },
            {
              name: "Days with cautions only",
              color: "var(--warning-fg)",
              values: summary.days.map((day) => (day.worst === "Caution" ? day.count : 0)),
            },
          ]}
        />
      </section>
    </div>
  );
}

/** Props for one figure. */
interface FigureProps {
  label: string;
  /** Shows the alerts behind the figure: the list's filter it sets (VEY-82). */
  onOpen(): void;
  value: ReactNode;
  detail: string;
  help: string;
  tone?: "warn" | "bad";
}

/** One figure: its name with its question mark, the figure, and a line under it. The whole tile shows its alerts. */
function Figure({ label, onOpen, value, detail, help, tone }: FigureProps) {
  return (
    <div className={["cn-figure", tone].filter(Boolean).join(" ")}>
      <div className="cn-figure-name">
        <button type="button" className="cn-figure-link" onClick={onOpen}>
          {label}
        </button>
        <HelpTip label={label}>{help}</HelpTip>
      </div>
      <div className="cn-figure-value">{value}</div>
      <div className="cn-figure-detail">{detail}</div>
    </div>
  );
}

/** The alerts as a table, a heading row for each day. */
function AlertTable({ items, now }: { items: AlertListItem[] | null; now: Date | null }) {
  if (items === null) {
    return <p className="cn-empty">Reading the alerts…</p>;
  }

  if (items.length === 0) {
    return <p className="cn-empty">No alert matches these filters.</p>;
  }

  const days: { day: string; items: AlertListItem[] }[] = [];

  for (const item of items) {
    const day = localDay(item.openedAt);
    const last = days.at(-1);

    if (last && last.day === day) {
      last.items.push(item);
    } else {
      days.push({ day, items: [item] });
    }
  }

  return (
    <div className="cn-table-scroll">
      <table className="cn-table">
        <thead>
          <tr>
            <th scope="col">Severity</th>
            <th scope="col">Alert</th>
            <th scope="col" className="wide advanced-only">
              Rule
            </th>
            <th scope="col" className="num">
              Worst
            </th>
            <th scope="col" className="wide">
              Open
            </th>
            <th scope="col" className="num wide advanced-only">
              Lasted
            </th>
          </tr>
        </thead>
        {days.map((group) => (
          <tbody key={group.day}>
            <tr className="cn-table-day">
              <th scope="rowgroup" colSpan={6}>
                {localDayName(group.day, now ?? new Date(0))}
              </th>
            </tr>
            {group.items.map((item) => {
              const open = item.resolvedAt === null;
              const end = item.resolvedAt ?? now?.toISOString() ?? item.updatedAt;

              return (
                <tr key={item.id}>
                  <td>
                    <Link
                      className={item.worst === "Warning" ? "cn-pill bad" : "cn-pill warn"}
                      to={alertFixPath(item.id)}
                      title="What it means and how to fix it"
                    >
                      {item.worst === "Warning" ? "Warning" : "Caution"}
                    </Link>
                  </td>
                  <td>
                    <Link className="cn-table-name" to={`/alerts/${encodeURIComponent(item.id)}`}>
                      {alertTitle(item.rule, item.block, open ? item.value : item.peak, !open)}
                    </Link>
                    <small className="cn-table-sub">
                      {item.block.kind} · {item.environment ?? "no environment"}
                    </small>
                  </td>
                  <td className="wide advanced-only">
                    <Link className="cn-cell-link" to={rulePath(item.rule)}>
                      {ruleName(item.rule)}
                    </Link>
                  </td>
                  <td className="num">{formatMeasure(item.rule, item.peak)}</td>
                  <td className="wide">
                    {formatClock(item.openedAt)} – {open ? <b className="cn-open">now</b> : formatClock(end)}
                  </td>
                  <td className="num wide advanced-only">
                    {formatDuration(Date.parse(end) - Date.parse(item.openedAt))}
                  </td>
                </tr>
              );
            })}
          </tbody>
        ))}
      </table>
    </div>
  );
}

/** The built-in rules with their thresholds and how many alerts each raised. */
function Rules({ summary }: { summary: AlertSummary }) {
  const raised = summary.rules.filter((rule) => rule.count > 0 || rule.enabled);

  return (
    <section className="cn-card cn-rules advanced-only" aria-labelledby="cn-alert-rules">
      <header>
        <h2 id="cn-alert-rules">Built-in rules</h2>
        <HelpTip label="Built-in rules">
          The rules Veyra checks every 30 seconds, with their caution and warning thresholds as this installation sets
          them, and how many alerts each raised in the period. Admins change them in Settings → Alert rules.
        </HelpTip>
      </header>
      <div className="cn-table-scroll">
        <table className="cn-table">
          <thead>
            <tr>
              <th scope="col">Rule</th>
              <th scope="col">Caution</th>
              <th scope="col">Warning</th>
              <th scope="col" className="num">
                Alerts
              </th>
            </tr>
          </thead>
          <tbody>
            {raised.map((rule) => (
              <tr key={rule.rule} className={rule.enabled ? undefined : "cn-off"}>
                <td title={ruleMeasures(rule.rule, rule.windowSeconds)}>
                  <Link className="cn-cell-link" to={rulePath(rule.rule)}>
                    {ruleName(rule.rule)}
                  </Link>
                </td>
                <td className="cn-warn">{rule.enabled ? formatThreshold(rule.rule, rule.cautionThreshold) : "off"}</td>
                <td className="cn-bad">{rule.enabled ? formatThreshold(rule.rule, rule.warningThreshold) : "off"}</td>
                <td className="num">{rule.count}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
