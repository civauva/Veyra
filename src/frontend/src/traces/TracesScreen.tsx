import { useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router";

import { useShell } from "../app/shellContext";
import { formatClock, formatCount } from "../logs/format";
import "../logs/logs.css";
import { DurationChart, type PickedWindow } from "./DurationChart";
import { TraceFilterPanel } from "./TraceFilterPanel";
import { filtersFromSearch, filtersToSearch, traceWindow, type TraceFilters } from "./traceFilters";
import { formatDuration } from "./traceMath";
import "./traces.css";
import { TraceTable } from "./TraceTable";
import { useTraceList } from "./useTraceList";
import { ViewToggle } from "../app/ViewToggle";

/** A trace ID as people paste it: 32 hexadecimal characters. */
const traceIdPattern = /^[0-9a-f]{32}$/i;

/**
 * The trace list (ADR 0014, VEY-27 mockups): a chart of every trace of the window by duration, and the traces one row
 * each, newest first. Filters live in the address, so a view can be linked. The environment is the one chosen in the
 * top bar.
 */
export function TracesScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const filters = filtersFromSearch(searchParams);
  const { status } = useShell();
  const list = useTraceList(filters, status.environment);
  const navigate = useNavigate();
  const [filtersOpen, setFiltersOpen] = useState(false);
  const { from, to } = traceWindow(filters, list.now);
  const p95 = list.durations?.p95Ms ?? null;
  const points = list.durations?.points ?? [];
  const picked: PickedWindow | null =
    filters.from && filters.to
      ? {
          from: new Date(filters.from),
          to: new Date(filters.to),
          minMs: filters.minMs ?? 0,
          maxMs: filters.maxMs ?? Math.max(...points.map((point) => point.durationMs), 1),
        }
      : null;

  function apply(next: TraceFilters) {
    setSearchParams(filtersToSearch(next));
  }

  function pick(window: PickedWindow) {
    apply({
      ...filters,
      from: window.from.toISOString(),
      to: window.to.toISOString(),
      minMs: window.minMs,
      maxMs: window.maxMs,
    });
  }

  const slowest = Math.max(1, ...(list.items ?? []).map((item) => item.durationMs));
  const active = [
    filters.show !== "all",
    filters.services.length > 0,
    filters.minMs !== null || filters.maxMs !== null,
    filters.attributes.length > 0,
  ].filter(Boolean).length;

  return (
    <main className="traces">
      <h1 className="visually-hidden">Traces</h1>
      <TraceFilterPanel
        filters={filters}
        onChange={apply}
        services={list.services}
        failedCount={list.durations ? points.filter((point) => point.failed).length : null}
        slowCount={p95 === null ? null : points.filter((point) => !point.failed && point.durationMs >= p95).length}
        from={from}
        to={to}
        open={filtersOpen}
        onClose={() => setFiltersOpen(false)}
      />

      <div className="traces-main" aria-busy={list.loading}>
        <header className="traces-head">
          <div>
            <h2>Traces</h2>
            <p>Requests and jobs, one row per trace from its first span. Click a dot or a row to open it.</p>
          </div>
          <TraceSearch
            key={filters.name}
            name={filters.name}
            onName={(name) => apply({ ...filters, name })}
            onTrace={(traceId) => void navigate(`/traces/${traceId.toLowerCase()}`)}
          />
          <button
            type="button"
            className="button toolbar-filters"
            aria-expanded={filtersOpen}
            onClick={() => setFiltersOpen(true)}
          >
            Filters{active > 0 && ` · ${active}`}
          </button>
          <ViewToggle className="traces-view" />
        </header>

        {list.error && (
          <p className="explorer-error" role="alert">
            {list.error}
          </p>
        )}

        <section className="traces-chart" aria-labelledby="traces-chart-title">
          <div className="traces-chart-top">
            <h3 id="traces-chart-title" className="section-title">
              Duration of each trace
            </h3>
            {picked ? (
              <span className="trace-picked">
                {formatClock(picked.from)} – {formatClock(picked.to)} · {formatDuration(picked.minMs)} –{" "}
                {formatDuration(picked.maxMs)}
                <button
                  type="button"
                  className="link-button"
                  onClick={() => apply({ ...filters, from: null, to: null, minMs: null, maxMs: null })}
                >
                  Clear
                </button>
              </span>
            ) : (
              <span className="filter-note">Drag across the chart to pick traces by time and duration.</span>
            )}
            <span className="traces-legend" aria-hidden="true">
              <span>
                <i className="dc-ok" />
                ok
              </span>
              <span>
                <i className="dc-slow" />
                slower than p95
              </span>
              <span>
                <i className="dc-failed" />
                failed
              </span>
            </span>
          </div>
          <DurationChart
            points={points}
            p95Ms={p95}
            from={from}
            to={to}
            picked={picked}
            onPick={pick}
            onOpen={(point) => void navigate(`/traces/${point.traceId}?at=${encodeURIComponent(point.start)}`)}
          />
          {list.durations && list.durations.read >= list.durations.maxRead && (
            <p className="filter-note">
              The chart shows the newest {formatCount(list.durations.maxRead)} traces of the window.
            </p>
          )}
        </section>

        {list.items !== null && list.items.length === 0 && !list.error && (
          <p className="explorer-empty">
            No traces match. Widen the time range or remove a filter. Traces arrive over OTLP from the OpenTelemetry
            SDKs; the setup is in <code>docs/integrations</code>.
          </p>
        )}

        {list.items !== null && list.items.length > 0 && (
          <TraceTable items={list.items} slowestMs={slowest} p95Ms={p95} />
        )}

        <footer className="explorer-footer">
          <span>
            {list.items === null ? "Loading traces…" : `${formatCount(list.items.length)} traces · newest first`}
          </span>
          {list.hasOlder && (
            <button type="button" className="link-button" onClick={list.loadOlder} disabled={list.loading}>
              Show older traces
            </button>
          )}
        </footer>
      </div>
    </main>
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

/** One box for both: a pasted trace ID opens the trace, anything else filters by name. */
function TraceSearch({ name, onName, onTrace }: TraceSearchProps) {
  const [draft, setDraft] = useState(name);

  function submit(event: FormEvent) {
    event.preventDefault();
    const text = draft.trim();

    if (traceIdPattern.test(text)) {
      onTrace(text);
    } else {
      onName(text);
    }
  }

  return (
    <form role="search" className="search traces-search" onSubmit={submit}>
      <span className="search-icon" aria-hidden="true">
        ⌕
      </span>
      <input
        type="search"
        aria-label="Trace ID or name"
        placeholder="Trace ID or name"
        value={draft}
        onChange={(event) => {
          setDraft(event.target.value);

          if (event.target.value === "" && name !== "") {
            onName("");
          }
        }}
      />
      <span className="search-hint">Enter</span>
    </form>
  );
}
