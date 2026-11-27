import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";

import type { TraceItem, TraceService } from "../../../api/types";
import { useShell } from "../../../app/shellContext";
import { formatClock, formatCount } from "../../../logs/format";
import { termText } from "../../../logs/queryText";
import { changeTone, changeWords } from "../../../resources/resourceFormat";
import { DurationChart, type PickedWindow } from "../../../traces/DurationChart";
import {
  filtersFromSearch,
  filtersToSearch,
  isTraceId,
  traceWindow,
  type TraceFilters,
  type TraceOrder,
  type TraceShow,
} from "../../../traces/traceFilters";
import { formatDuration } from "../../../traces/traceMath";
import "../../../traces/traces.css";
import {
  clockSeconds,
  durationChoices,
  failedShare,
  orderChoices,
  showChoices,
  traceChange,
  traceStatus,
} from "../../../traces/traceWords";
import { useTraceList } from "../../../traces/useTraceList";
import { HelpTip } from "../../../ui/HelpTip";
import { showPart } from "../../../metrics/database/drill";
import { ScreenBar } from "../../../ui/ScreenBar";
import { useViewMode } from "../../../ui/ViewToggle";
import { SqlFigure } from "../database/sqlParts";
import "./consoleTraces.css";

/** The order Console's trace list opens on: failed first, then the slowest (ADR 0029). */
const consoleOrder: TraceOrder = "worst";

/**
 * Console's traces (VEY-79, ADR 0029): the window's figures, the filters as chips, the duration of every trace with
 * its p95, the slowest services, and the traces worst first. Every filter and the order are address parameters the
 * server applies; the figures and the ranking of the services come from the server. The time range is the top bar's.
 */
export function ConsoleTraces() {
  const [params, setParams] = useSearchParams();
  const filters = filtersFromSearch(params, consoleOrder);
  const { status } = useShell();
  const list = useTraceList(filters, status.environment, { summary: true, slowest: true });
  const navigate = useNavigate();
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const { from, to } = traceWindow(filters, list.now);
  const summary = list.summary;
  const p95 = list.durations?.p95Ms ?? null;
  const points = list.durations?.points ?? [];
  const banded = filters.minMs !== null || filters.maxMs !== null;
  const picked: PickedWindow | null =
    filters.from && filters.to && banded
      ? {
          from: new Date(filters.from),
          to: new Date(filters.to),
          minMs: filters.minMs ?? 0,
          maxMs: filters.maxMs ?? Math.max(...points.map((point) => point.durationMs), 1),
        }
      : null;

  function apply(next: TraceFilters) {
    setParams(filtersToSearch(next, consoleOrder));
  }

  const service = filters.services[0] ?? null;
  const logsOfServices = carryWindow(
    service ? `/logs?${new URLSearchParams({ q: termText("service", service) }).toString()}` : "/logs",
    params,
  );

  return (
    <main className="cn-screen sq tr">
      <ScreenBar trail={[{ label: "Traces" }]} view />
      <div className="cn-head">
        <div>
          <h1>
            Traces
            {summary && (summary.failed > 0 || summary.slow > 0) && (
              <button
                type="button"
                className={summary.failed > 0 ? "cn-pill bad" : "cn-pill warn"}
                onClick={() => apply({ ...filters, show: summary.failed > 0 ? "failed" : "slow" })}
              >
                {formatCount(summary.failed)} failed · {formatCount(summary.slow)} slow
              </button>
            )}
          </h1>
          <p>
            Requests across services. Failed and slow traces first; every filter and the order are applied by the
            server.
          </p>
        </div>
        <div className="cn-head-actions">
          <Link className="cn-button" to={logsOfServices}>
            {service ? `Logs of ${service}` : "Logs of these services"}
          </Link>
        </div>
      </div>

      <section className="sq-figures sq-figures-five" aria-label="Figures">
        <SqlFigure
          label="Traces"
          value={summary ? formatCount(summary.traces) : "—"}
          detail={
            summary && (
              <span className={`sq-change ${changeTone(traceChange(summary), false)}`}>
                {changeWords(traceChange(summary))}
              </span>
            )
          }
          help="The traces of the time range under the filters, one per request or job from its first span, and how many more or fewer than in the period of the same length before. The server counts them."
          onOpen={() => apply({ ...filters, show: "all" })}
        />
        <SqlFigure
          label="Failed"
          value={
            summary ? (
              <>
                {formatCount(summary.failed)} <small>· {failedShare(summary.failed, summary.traces)}</small>
              </>
            ) : (
              "—"
            )
          }
          detail="of the traces in the range"
          tone={summary && summary.failed > 0 ? "bad" : undefined}
          help="Traces whose request or job failed: its first span ended in an error. Open one for the span that failed and its exception."
          onOpen={() => apply({ ...filters, show: "failed" })}
        />
        <SqlFigure
          label="Slower than p95"
          value={summary ? formatCount(summary.slow) : "—"}
          detail={summary?.p95Ms != null ? `p95 ${formatDuration(summary.p95Ms)} over the range` : "no p95 yet"}
          tone={summary && summary.slow > 0 ? "warn" : undefined}
          help="Traces that took at least the 95th percentile of the range: the slowest 5%. The dashed line on the chart is that p95."
          onOpen={() => apply({ ...filters, show: "slow" })}
        />
        <SqlFigure
          label="Median"
          value={summary?.p50Ms != null ? formatDuration(summary.p50Ms) : "—"}
          detail="half of the traces were faster"
          help="The median duration of the traces in the range: half took less, half took more. A rising median means most requests got slower, not only a few."
          onOpen={() => showPart("tr-chart-card")}
        />
        <SqlFigure
          label="Services"
          value={summary ? formatCount(summary.services) : "—"}
          detail="that sent traces"
          help="The services whose spans started a trace in the range under the filters."
          onOpen={() => showPart("tr-services-card")}
        />
      </section>

      <Filters
        filters={filters}
        services={list.services}
        advanced={advanced}
        onChange={apply}
        onTrace={(traceId) => void navigate(`/traces/${traceId}`)}
      />

      {list.error && (
        <p className="cn-error" role="alert">
          Could not refresh: {list.error}
        </p>
      )}

      <div className="sq-row tr-row">
        <section id="tr-chart-card" className="cn-card" aria-labelledby="tr-chart" tabIndex={-1}>
          <header>
            <h2 id="tr-chart">Duration over time</h2>
            <HelpTip label="Duration over time">
              Each dot is a trace of the range: when it started, and how long it took on a logarithmic scale. Red ones
              failed and amber ones are slower than the 95th percentile, the dashed line. The server sends every failed
              and slow trace and a sample of the rest. Drag a box to narrow the list to a time and a duration, or click
              a dot to open its trace.
            </HelpTip>
            {picked ? (
              <span className="cn-meta">
                {formatClock(picked.from)}–{formatClock(picked.to)} · {formatDuration(picked.minMs)}–
                {formatDuration(picked.maxMs)}{" "}
                <button
                  type="button"
                  className="link-button"
                  onClick={() => apply({ ...filters, from: null, to: null, minMs: null, maxMs: null })}
                >
                  Clear
                </button>
              </span>
            ) : (
              list.durations && <span className="cn-meta">{formatCount(list.durations.read)} traces read</span>
            )}
          </header>
          <div className="tr-chart">
            <DurationChart
              points={points}
              p95Ms={p95}
              read={list.durations?.read ?? 0}
              failed={list.durations?.failed ?? 0}
              slow={list.durations?.slow ?? 0}
              from={from}
              to={to}
              picked={picked}
              onPick={(window) =>
                apply({
                  ...filters,
                  from: window.from.toISOString(),
                  to: window.to.toISOString(),
                  minMs: window.minMs,
                  maxMs: window.maxMs,
                })
              }
              onOpen={(point) => void navigate(`/traces/${point.traceId}?at=${encodeURIComponent(point.start)}`)}
            />
          </div>
          <ul className="tr-legend">
            <li>
              <i className="ok" aria-hidden="true" />
              OK
            </li>
            <li>
              <i className="slow" aria-hidden="true" />
              Slower than p95
            </li>
            <li>
              <i className="bad" aria-hidden="true" />
              Failed
            </li>
            <li className="tr-legend-note">Drag to narrow the time and duration · click a dot to open it</li>
          </ul>
        </section>

        <SlowestServices
          services={list.services}
          chosen={service}
          onPick={(name) => apply({ ...filters, services: name === service ? [] : [name] })}
        />
      </div>

      <TraceList
        items={list.items}
        p95Ms={p95}
        total={summary?.traces ?? null}
        order={filters.order}
        hasMore={list.hasOlder}
        loading={list.loading}
        onMore={list.loadOlder}
      />
    </main>
  );
}

/** Props for {@link Filters}. */
interface FiltersProps {
  /** What the person chose. */
  filters: TraceFilters;
  /** The services of the window, from the server, to pick from. */
  services: TraceService[];
  /** Whether the Advanced view is on, which adds the duration and attribute filters. */
  advanced: boolean;
  /** Called with the filters to apply. */
  onChange: (next: TraceFilters) => void;
  /** Called with a pasted trace ID to open. */
  onTrace: (traceId: string) => void;
}

/**
 * The filters as chips: what to show, the service, the duration, the order, the attributes, and the search. Duration
 * and attributes belong to the Advanced view, but a filter in use always shows, so a link never hides it.
 */
function Filters({ filters, services, advanced, onChange, onTrace }: FiltersProps) {
  const [adding, setAdding] = useState(false);
  const chosen = filters.services[0] ?? "";
  const names = services.some((one) => one.service === chosen) || chosen === "" ? services : [];
  const durations = durationChoices.some(([value]) => value === filters.minMs)
    ? durationChoices
    : [...durationChoices, [filters.minMs, `> ${formatDuration(filters.minMs ?? 0)}`] as [number | null, string]];

  return (
    <div className="tr-filters" role="group" aria-label="Filters">
      <label className={filters.show === "all" ? "cn-filter" : "cn-filter on"}>
        <span>Show:</span>
        <select
          aria-label="Show"
          value={filters.show}
          onChange={(event) => onChange({ ...filters, show: event.target.value as TraceShow })}
        >
          {showChoices.map(([value, words]) => (
            <option key={value} value={value}>
              {words}
            </option>
          ))}
        </select>
      </label>
      <label className={chosen ? "cn-filter on" : "cn-filter"}>
        <span>Service:</span>
        <select
          aria-label="Service"
          value={chosen}
          onChange={(event) => onChange({ ...filters, services: event.target.value ? [event.target.value] : [] })}
        >
          <option value="">All</option>
          {chosen && names.length === 0 && <option value={chosen}>{chosen}</option>}
          {names.map((one) => (
            <option key={one.service} value={one.service}>
              {one.service}
            </option>
          ))}
        </select>
      </label>
      {(advanced || filters.minMs !== null) && (
        <label className={filters.minMs !== null ? "cn-filter on" : "cn-filter"}>
          <span>Duration:</span>
          <select
            aria-label="Duration"
            value={filters.minMs ?? ""}
            onChange={(event) =>
              onChange({
                ...filters,
                minMs: event.target.value === "" ? null : Number(event.target.value),
                maxMs: null,
              })
            }
          >
            {durations.map(([value, words]) => (
              <option key={words} value={value ?? ""}>
                {words}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className={filters.order === consoleOrder ? "cn-filter on" : "cn-filter"}>
        <span>Order:</span>
        <select
          aria-label="Order"
          value={filters.order}
          onChange={(event) => onChange({ ...filters, order: event.target.value as TraceOrder })}
        >
          {orderChoices.map(([value, words]) => (
            <option key={value} value={value}>
              {words}
            </option>
          ))}
        </select>
      </label>
      {filters.attributes.map((attribute, index) => (
        <span key={`${attribute.key}:${attribute.value}`} className="cn-filter on tr-attribute">
          <span>{attribute.key}:</span>
          <b>{attribute.value}</b>
          <button
            type="button"
            aria-label={`Remove ${attribute.key}`}
            onClick={() => onChange({ ...filters, attributes: filters.attributes.filter((_, at) => at !== index) })}
          >
            ×
          </button>
        </span>
      ))}
      {advanced &&
        (adding ? (
          <AttributeForm
            onAdd={(key, value) => {
              setAdding(false);
              onChange({ ...filters, attributes: [...filters.attributes, { key, value }] });
            }}
            onCancel={() => setAdding(false)}
          />
        ) : (
          <button type="button" className="cn-filter tr-add" onClick={() => setAdding(true)}>
            + Attribute
          </button>
        ))}
      <TraceSearch
        key={filters.name}
        name={filters.name}
        onName={(name) => onChange({ ...filters, name })}
        onTrace={onTrace}
      />
    </div>
  );
}

/** Props for {@link AttributeForm}. */
interface AttributeFormProps {
  /** Called with the attribute the root span must have. */
  onAdd: (key: string, value: string) => void;
  /** Called when the person gives up. */
  onCancel: () => void;
}

/** A box for one attribute as `key:value`, added with Enter and dropped with Escape. */
function AttributeForm({ onAdd, onCancel }: AttributeFormProps) {
  const [text, setText] = useState("");

  function submit(event: FormEvent) {
    event.preventDefault();
    const at = text.indexOf(":");

    if (at > 0 && at < text.length - 1) {
      onAdd(text.slice(0, at).trim(), text.slice(at + 1).trim());
    }
  }

  return (
    <form className="tr-attribute-form" onSubmit={submit}>
      <input
        className="cn-find"
        aria-label="Attribute, as key:value"
        placeholder="http.route:/checkout"
        value={text}
        // The person asked for the box, so it takes the keyboard at once.
        autoFocus
        onChange={(event) => setText(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            onCancel();
          }
        }}
      />
    </form>
  );
}

/** Props for {@link TraceSearch}. */
interface TraceSearchProps {
  /** The name filter now. */
  name: string;
  /** Called with a name to filter by. */
  onName: (name: string) => void;
  /** Called with a trace ID to open. */
  onTrace: (traceId: string) => void;
}

/** One box for both: a pasted trace ID opens the trace, anything else filters by the operation's name. */
function TraceSearch({ name, onName, onTrace }: TraceSearchProps) {
  const [draft, setDraft] = useState(name);

  function submit(event: FormEvent) {
    event.preventDefault();
    const text = draft.trim();

    if (isTraceId(text)) {
      onTrace(text.toLowerCase());
    } else {
      onName(text);
    }
  }

  return (
    <form role="search" className="tr-search" onSubmit={submit}>
      <input
        type="search"
        className="cn-find"
        aria-label="Operation, or paste a trace ID"
        placeholder="Operation, or paste a trace ID"
        value={draft}
        onChange={(event) => {
          setDraft(event.target.value);

          if (event.target.value === "" && name !== "") {
            onName("");
          }
        }}
      />
    </form>
  );
}

/** Props for {@link SlowestServices}. */
interface SlowestServicesProps {
  /** The services, slowest p95 first, as the server ranked them. */
  services: TraceService[];
  /** The service the list is filtered by, or null. */
  chosen: string | null;
  /** Called with a service to filter by, or to stop filtering by when it is the chosen one. */
  onPick: (service: string) => void;
}

/** The services of the range, slowest p95 first, each filtering the list by itself when clicked. */
function SlowestServices({ services, chosen, onPick }: SlowestServicesProps) {
  return (
    <section id="tr-services-card" className="cn-card" aria-labelledby="tr-services" tabIndex={-1}>
      <header>
        <h2 id="tr-services">Slowest services</h2>
        <HelpTip label="Slowest services">
          The services whose requests started traces in the range, slowest 95th percentile first, with how many traces
          each started and the share of them that failed. The server ranks them under the same filters, every service
          kept. Click one to filter the list by it, and again to stop.
        </HelpTip>
        <span className="cn-meta">by p95 · click to filter</span>
      </header>
      {services.length === 0 ? (
        <p className="cn-empty">No service started a trace in the range.</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">Service</th>
                <th scope="col" className="num">
                  Traces
                </th>
                <th scope="col" className="num">
                  p95
                </th>
                <th scope="col" className="num">
                  Failed
                </th>
              </tr>
            </thead>
            <tbody>
              {services.map((one) => (
                <tr key={one.service} className={one.service === chosen ? "tr-chosen" : undefined}>
                  <td>
                    <button
                      type="button"
                      className="tr-service"
                      aria-pressed={one.service === chosen}
                      onClick={() => onPick(one.service)}
                    >
                      {one.service}
                    </button>
                  </td>
                  <td className="num">{formatCount(one.traces)}</td>
                  <td className="num">{one.p95Ms === null ? "—" : formatDuration(one.p95Ms)}</td>
                  <td className={one.failed > 0 ? "num cn-bad" : "num"}>{failedShare(one.failed, one.traces)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** Props for {@link TraceList}. */
interface TraceListProps {
  /** The traces loaded so far, in the server's order; null before the first page. */
  items: TraceItem[] | null;
  /** The range's p95, which marks a trace slow. */
  p95Ms: number | null;
  /** The traces the server counted under the filters, or null before the figures arrive. */
  total: number | null;
  /** The order the server listed them in. */
  order: TraceOrder;
  /** Whether the server has more. */
  hasMore: boolean;
  /** Whether a page is loading. */
  loading: boolean;
  /** Loads the next page. */
  onMore: () => void;
}

/** The traces, one row each, in the server's order, with Load more. */
function TraceList({ items, p95Ms, total, order, hasMore, loading, onMore }: TraceListProps) {
  return (
    <section className="cn-card" aria-labelledby="tr-list">
      <header>
        <h2 id="tr-list">Traces</h2>
        <HelpTip label="Traces">
          One row per trace, from its first span: when it started, its operation, the service it started in, how many
          spans arrived, and how long it took. Worst first lists failed traces, then the slowest; the server orders and
          pages them. Open one for its waterfall.
        </HelpTip>
        <span className="cn-meta">{order === "worst" ? "Worst first: failed, then slowest" : "Newest first"}</span>
      </header>
      {items === null ? (
        <p className="cn-empty">Reading the traces…</p>
      ) : items.length === 0 ? (
        <p className="cn-empty">
          No trace matches the filters in the range. Traces arrive over OTLP from the OpenTelemetry SDKs.
        </p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table" aria-label="Traces">
            <thead>
              <tr>
                <th scope="col">Start</th>
                <th scope="col">Operation</th>
                <th scope="col" className="wide">
                  Service
                </th>
                <th scope="col" className="num wide">
                  Spans
                </th>
                <th scope="col" className="num">
                  Duration
                </th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const state = traceStatus(item, p95Ms);

                return (
                  <tr key={item.traceId}>
                    <td className="mono">{clockSeconds(item.start)}</td>
                    <td>
                      <Link
                        className="cn-table-name"
                        to={`/traces/${item.traceId}?at=${encodeURIComponent(item.start)}`}
                      >
                        {item.name}
                      </Link>
                    </td>
                    <td className="wide">
                      <Link className="cn-cell-link" to={`/metrics/services/${encodeURIComponent(item.service)}`}>
                        {item.service}
                      </Link>
                    </td>
                    <td className="num wide">{formatCount(item.spans)}</td>
                    <td className={state === "failed" ? "num cn-bad" : "num"}>{formatDuration(item.durationMs)}</td>
                    <td>
                      <span
                        className={
                          state === "failed" ? "cn-pill bad" : state === "slow" ? "cn-pill warn" : "cn-pill ok"
                        }
                      >
                        {state === "failed" ? "Failed" : state === "slow" ? "Slow" : "OK"}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {items !== null && items.length > 0 && (
        <footer>
          <span>
            {formatCount(items.length)}
            {total !== null ? ` of ${formatCount(total)}` : ""}
          </span>
          {hasMore && (
            <button type="button" className="link-button" disabled={loading} onClick={onMore}>
              {loading ? "Loading…" : "Load more →"}
            </button>
          )}
        </footer>
      )}
    </section>
  );
}

/**
 * A path with the range of the address added, keeping the query the path already has.
 *
 * @param path - The path, perhaps with a query.
 * @param current - The address's query parameters now.
 * @returns The path with the range.
 */
function carryWindow(path: string, current: URLSearchParams): string {
  const [base = path, query = ""] = path.split("?");
  const merged = new URLSearchParams(query);

  for (const key of ["range", "from", "to"]) {
    const value = current.get(key);

    if (value !== null) {
      merged.set(key, value);
    }
  }

  const search = merged.toString();

  return search ? `${base}?${search}` : base;
}
