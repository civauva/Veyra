import { useState, type FormEvent, type ReactNode } from "react";
import { useSearchParams } from "react-router";

import type { LogEntry, LogQueryField, LogQueryTerm, LogSummary, Severity } from "../../api/types";
import { useShell } from "../../app/shellContext";
import { exportLogs } from "../../logs/exportLink";
import { formatClock, formatCount, formatTime } from "../../logs/format";
import { levelName, levelWord, pickableLevels, severityOfWord } from "../../logs/levelWords";
import { filtersFromUrl } from "../../logs/logFilters";
import { addTerm, removeTerm, replaceTerm, termText } from "../../logs/queryText";
import { useLogQueryTerms, type QueryProblem } from "../../logs/useLogQueryTerms";
import { useLogSearch } from "../../logs/useLogSearch";
import { useSavedViews } from "../../logs/useSavedViews";
import { StackedBars } from "../../overview/StackedBars";
import { shortCount } from "../../resources/resourceFormat";
import { HelpTip } from "../../ui/HelpTip";
import { ScreenBar } from "../../ui/ScreenBar";
import { useViewMode } from "../../ui/ViewToggle";
import { ConsoleLogInspector } from "./ConsoleLogInspector";
import { ConsoleMenu } from "./ConsoleMenu";
import "./consoleLogs.css";

/** The colours of the levels, the same in the volume chart, the By level bars, and the table. */
const levelColor: Partial<Record<Severity, string>> = {
  Debug: "var(--faint)",
  Information: "var(--info-fg)",
  Warning: "var(--warning-fg)",
  Error: "var(--error-fg)",
  Fatal: "var(--error-fg)",
};

/**
 * Console's Log explorer (VEY-74, reference screenshot 6). The query bar holds every filter as text the server parses
 * (ADR 0024); the chips show the terms the server found and add or remove them; the time is the top bar's. Below, the
 * volume by level, the totals by level, the events, and the inspector of the chosen one.
 */
export function ConsoleLogs() {
  const [viewMode] = useViewMode();
  const [params, setParams] = useSearchParams();
  const { status, time } = useShell();
  const filters = filtersFromUrl(params);
  const query = filters.query;
  const search = useLogSearch(filters, time?.live ?? false, status.environment);
  const { terms, problem } = useLogQueryTerms(query);
  const views = useSavedViews("logs");
  const [draft, setDraft] = useState({ for: query, text: query });
  const [chosenId, setChosenId] = useState<number | null>(null);
  const text = draft.for === query ? draft.text : query;
  const items = search.items;
  const chosenIndex = Math.max(
    0,
    items.findIndex((item) => item.id === chosenId),
  );
  const chosen: LogEntry | undefined = items[chosenIndex];
  const currentView = views.views?.find((view) => view.query.trim() === query.trim()) ?? null;

  /** Runs a query: it goes into the address, so a link or a reload shows the same search. */
  function run(next: string) {
    const changed = new URLSearchParams(params);
    const trimmed = next.trim();

    if (trimmed) {
      changed.set("q", trimmed);
    } else {
      changed.delete("q");
    }

    changed.delete("around");
    setDraft({ for: trimmed, text: trimmed });
    setChosenId(null);
    setParams(changed, { replace: true });
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    run(text);
  }

  /** Puts a term in the place of the first one on the same field, or adds it. */
  function setTerm(field: LogQueryField, term: string) {
    const existing = terms.find((one) => one.field === field);

    run(existing ? replaceTerm(query, existing, term) : addTerm(query, term));
  }

  function around(entry: LogEntry) {
    const changed = new URLSearchParams(params);
    changed.set("around", entry.timestamp);
    setParams(changed, { replace: true });
  }

  const window = { from: search.from.toISOString(), to: search.to.toISOString() };
  const total = search.summary?.total ?? null;

  return (
    <main className="cn-screen cn-logs" data-view={viewMode}>
      <ScreenBar trail={[{ label: "Logs" }]} view />
      <div className="cn-head">
        <div>
          <h1>Logs</h1>
          <p>Search and filter structured events. Every filter runs on the server.</p>
        </div>
        <div className="cn-head-actions">
          {time?.live && (
            <span className="cn-live-pill">
              <span className="cn-dot ok" aria-hidden="true" />
              Live
            </span>
          )}
          <ViewsMenu
            views={views.views}
            current={currentView?.id ?? null}
            onOpen={(viewQuery) => run(viewQuery)}
            onDelete={(id) => void views.remove(id)}
          />
          <SaveViewMenu query={query} onSave={views.save} />
          <ConsoleMenu button={<>Export ▾</>} buttonClass="cn-button" menuLabel="Export" menuClass="cn-menu-export">
            {(close) => (
              <>
                <p className="cn-menu-cap">Every matching record · from the server</p>
                {(["ndjson", "csv"] as const).map((format) => (
                  <button
                    key={format}
                    type="button"
                    className="cn-menu-option"
                    onClick={() => {
                      exportLogs(filters, status.environment, format);
                      close();
                    }}
                  >
                    {format === "ndjson" ? "NDJSON, one record a line" : "CSV, for a spreadsheet"}
                  </button>
                ))}
                <p className="cn-menu-note">Up to the export limit an Admin sets in Settings.</p>
              </>
            )}
          </ConsoleMenu>
        </div>
      </div>

      <form className="cn-query" role="search" aria-label="Query" onSubmit={submit}>
        <label className={problem ? "cn-query-input bad" : "cn-query-input"}>
          <span className="visually-hidden">Query</span>
          <span aria-hidden="true">⌕</span>
          <input
            value={text}
            spellCheck={false}
            placeholder='service:orders AND level>=warning "timed out"'
            aria-invalid={problem ? true : undefined}
            aria-describedby={problem ? "cn-query-problem" : undefined}
            onChange={(event) => setDraft({ for: query, text: event.target.value })}
          />
        </label>
        <button type="submit" className="cn-button primary">
          ▶ Run
        </button>
        <HelpTip label="Query">
          Terms are joined with AND, or a space: <code>service:</code>, <code>env:</code>, <code>host:</code>,{" "}
          <code>level:</code> or <code>level&gt;=</code>, <code>trace:</code>, <code>span:</code>, any attribute as{" "}
          <code>key:value</code>, and words or &quot;quoted phrases&quot; of the message. The server parses the query,
          so a link with it shows the same search. The time comes from the top bar.
        </HelpTip>
      </form>
      {problem && <QueryProblemLine problem={problem} />}

      <Chips
        terms={terms}
        summary={search.summary}
        environment={status.environment}
        onSet={setTerm}
        onAdd={(term) => run(addTerm(query, term))}
        onRemove={(term) => run(removeTerm(query, term))}
      />

      {search.error && (
        <p className="cn-error" role="alert">
          Could not search: {search.error}
        </p>
      )}

      <div className="cn-columns cn-logs-columns">
        <Volume summary={search.summary} />
        <ByLevel summary={search.summary} onPick={(level) => setTerm("level", termText("level", levelWord[level]))} />
      </div>

      <div className="cn-columns cn-logs-columns">
        <Events
          items={items}
          chosenId={chosen?.id ?? null}
          total={total}
          loading={search.loading}
          hasMore={search.hasMore}
          loadingMore={search.loadingMore}
          onChoose={setChosenId}
          onMore={search.loadMore}
          aroundNote={
            filters.around
              ? `± 1 minute around ${formatTime(filters.around)}`
              : `${formatClock(search.from)} – ${formatClock(search.to)}`
          }
        />
        {chosen ? (
          <ConsoleLogInspector
            entry={chosen}
            index={chosenIndex}
            total={total}
            window={window}
            onPrevious={chosenIndex > 0 ? () => setChosenId(items[chosenIndex - 1]?.id ?? null) : null}
            onNext={chosenIndex < items.length - 1 ? () => setChosenId(items[chosenIndex + 1]?.id ?? null) : null}
            onTrace={(traceId) => setTerm("trace", termText("trace", traceId))}
            onAround={around}
          />
        ) : (
          <section className="cn-card cn-inspector" aria-label="Event">
            <p className="cn-empty">{search.loading ? "Searching…" : "No event to show."}</p>
          </section>
        )}
      </div>
    </main>
  );
}

/** Says where and why the server could not parse the query. */
function QueryProblemLine({ problem }: { problem: QueryProblem }) {
  return (
    <p id="cn-query-problem" className="cn-query-problem" role="alert">
      ⚠ {problem.position === null ? "" : `At ${problem.position + 1}: `}
      {problem.message}
    </p>
  );
}

/** Props for {@link Chips}. */
interface ChipsProps {
  terms: LogQueryTerm[];
  summary: LogSummary | null;
  environment: string | null;
  onSet(field: LogQueryField, term: string): void;
  onAdd(term: string): void;
  onRemove(term: LogQueryTerm): void;
}

/** The chips: each shows the term the server found for its field, offers values, and removes it with ×. */
function Chips({ terms, summary, environment, onSet, onAdd, onRemove }: ChipsProps) {
  const termOf = (field: LogQueryField) => terms.find((term) => term.field === field);
  const level = termOf("level");
  const levelValue = level
    ? `${levelName[severityOfWord(level.value) ?? "Information"]}${level.op === ">=" ? " +" : ""}`
    : "Any";
  const attributes = terms.filter((term) => term.field === "attribute");

  return (
    <div className="cn-chips" aria-label="Filters" role="group">
      <Chip name="Level" value={levelValue} term={level} onRemove={onRemove}>
        {(close) =>
          pickableLevels.map((severity) => (
            <button
              key={severity}
              type="button"
              className="cn-menu-option"
              onClick={() => {
                onSet("level", termText("level", levelWord[severity], ">="));
                close();
              }}
            >
              {levelName[severity]} or worse
            </button>
          ))
        }
      </Chip>
      <Chip name="Service" value={termOf("service")?.value ?? "All"} term={termOf("service")} onRemove={onRemove}>
        {(close) =>
          (summary?.services ?? []).slice(0, 12).map((service) => (
            <button
              key={service.name}
              type="button"
              className="cn-menu-option"
              onClick={() => {
                onSet("service", termText("service", service.name));
                close();
              }}
            >
              {service.name}
              <kbd>{shortCount(service.count)}</kbd>
            </button>
          ))
        }
      </Chip>
      <Chip
        name="Environment"
        value={termOf("environment")?.value ?? environment ?? "All"}
        term={termOf("environment")}
        onRemove={onRemove}
      >
        {(close) => (
          <TermForm
            label="Environment"
            field="environment"
            onAdd={(term) => onSet("environment", term)}
            close={close}
          />
        )}
      </Chip>
      <Chip name="Host" value={termOf("host")?.value ?? "All"} term={termOf("host")} onRemove={onRemove}>
        {(close) => <TermForm label="Host" field="host" onAdd={(term) => onSet("host", term)} close={close} />}
      </Chip>
      <Chip
        name="Trace"
        value={termOf("trace") ? `${termOf("trace")?.value.slice(0, 8)}…` : "Any"}
        term={termOf("trace")}
        onRemove={onRemove}
      >
        {(close) => <TermForm label="Trace ID" field="trace" onAdd={(term) => onSet("trace", term)} close={close} />}
      </Chip>
      {attributes.map((term) => (
        <span key={`${term.start}`} className="cn-chip on">
          {term.key}: <b>{term.value}</b>
          <button type="button" className="cn-chip-x" aria-label={`Remove ${term.key}`} onClick={() => onRemove(term)}>
            ×
          </button>
        </span>
      ))}
      <ConsoleMenu button={<>+ Attribute</>} buttonClass="cn-chip add" menuLabel="Add an attribute filter">
        {(close) => <AttributeForm onAdd={onAdd} close={close} />}
      </ConsoleMenu>
      <span className="cn-chips-note">Time from the top bar · counts and order from the server</span>
    </div>
  );
}

/** Props for {@link Chip}. */
interface ChipProps {
  name: string;
  value: string;
  term: LogQueryTerm | undefined;
  onRemove(term: LogQueryTerm): void;
  children(close: () => void): ReactNode;
}

/** One chip: its field and value, a menu of values, and × while the query has a term on its field. */
function Chip({ name, value, term, onRemove, children }: ChipProps) {
  return (
    <span className={term ? "cn-chip-wrap on" : "cn-chip-wrap"}>
      <ConsoleMenu
        button={
          <>
            {name}: <b>{value}</b>
          </>
        }
        buttonClass={term ? "cn-chip on" : "cn-chip"}
        menuLabel={name}
      >
        {children}
      </ConsoleMenu>
      {term && (
        <button
          type="button"
          className="cn-chip-x"
          aria-label={`Remove the ${name.toLowerCase()} filter`}
          onClick={() => onRemove(term)}
        >
          ×
        </button>
      )}
    </span>
  );
}

/** A value typed into a chip's menu. */
function TermForm({
  label,
  field,
  onAdd,
  close,
}: {
  label: string;
  field: LogQueryField;
  onAdd(term: string): void;
  close(): void;
}) {
  const [value, setValue] = useState("");

  return (
    <form
      className="cn-menu-form"
      onSubmit={(event) => {
        event.preventDefault();

        if (value.trim()) {
          onAdd(termText(field, value.trim()));
          close();
        }
      }}
    >
      <label>
        {label}
        <input value={value} onChange={(event) => setValue(event.target.value)} />
      </label>
      <button type="submit" className="cn-button" disabled={!value.trim()}>
        Filter
      </button>
    </form>
  );
}

/** An attribute's name and value, typed into the + Attribute menu. */
function AttributeForm({ onAdd, close }: { onAdd(term: string): void; close(): void }) {
  const [key, setKey] = useState("");
  const [value, setValue] = useState("");
  const ready = /^[\w.\-/]+$/.test(key.trim()) && value.trim() !== "";

  return (
    <form
      className="cn-menu-form"
      onSubmit={(event) => {
        event.preventDefault();

        if (ready) {
          onAdd(termText("attribute", value.trim(), ":", key.trim()));
          close();
        }
      }}
    >
      <label>
        Attribute
        <input value={key} placeholder="http.route" onChange={(event) => setKey(event.target.value)} />
      </label>
      <label>
        Value
        <input value={value} placeholder="/orders" onChange={(event) => setValue(event.target.value)} />
      </label>
      <button type="submit" className="cn-button" disabled={!ready}>
        Filter
      </button>
    </form>
  );
}

/** Props for {@link ViewsMenu}. */
interface ViewsMenuProps {
  views: ReturnType<typeof useSavedViews>["views"];
  current: string | null;
  onOpen(query: string): void;
  onDelete(id: string): void;
}

/** The saved views: one's own, then those others shared. Opening one runs its query in the top bar's range. */
function ViewsMenu({ views, current, onOpen, onDelete }: ViewsMenuProps) {
  const mine = (views ?? []).filter((view) => view.mine);
  const shared = (views ?? []).filter((view) => !view.mine);
  const currentName = views?.find((view) => view.id === current)?.name;

  return (
    <ConsoleMenu
      button={
        <>
          View: <b>{currentName ?? "none"}</b> ▾
        </>
      }
      buttonClass="cn-button cn-views"
      menuLabel="Saved views"
      menuClass="cn-menu-views"
    >
      {(close) => (
        <>
          <p className="cn-menu-cap">My views</p>
          {mine.length === 0 && <p className="cn-menu-note">None yet. Save the current query to keep it.</p>}
          {mine.map((view) => (
            <div key={view.id} className="cn-view-row">
              <button
                type="button"
                className="cn-menu-option"
                aria-pressed={view.id === current}
                onClick={() => {
                  onOpen(view.query);
                  close();
                }}
              >
                {view.name}
                {view.shared && <kbd>shared</kbd>}
              </button>
              <button
                type="button"
                className="cn-view-delete"
                aria-label={`Delete ${view.name}`}
                onClick={() => onDelete(view.id)}
              >
                ×
              </button>
            </div>
          ))}
          <p className="cn-menu-cap">Shared with everyone</p>
          {shared.length === 0 && <p className="cn-menu-note">No one shared a view yet.</p>}
          {shared.map((view) => (
            <button
              key={view.id}
              type="button"
              className="cn-menu-option"
              aria-pressed={view.id === current}
              onClick={() => {
                onOpen(view.query);
                close();
              }}
            >
              {view.name}
              <kbd>{view.ownerName ?? "someone"}</kbd>
            </button>
          ))}
          <p className="cn-menu-note">A view keeps the query, not the time range.</p>
        </>
      )}
    </ConsoleMenu>
  );
}

/** Saves the current query as a view, with a name and whether everyone may open it. */
function SaveViewMenu({ query, onSave }: { query: string; onSave: ReturnType<typeof useSavedViews>["save"] }) {
  return (
    <ConsoleMenu button={<>Save view</>} buttonClass="cn-button" menuLabel="Save view" menuClass="cn-menu-views">
      {(close) => <SaveViewForm query={query} onSave={onSave} close={close} />}
    </ConsoleMenu>
  );
}

/** The form inside {@link SaveViewMenu}. */
function SaveViewForm({
  query,
  onSave,
  close,
}: {
  query: string;
  onSave: ReturnType<typeof useSavedViews>["save"];
  close(): void;
}) {
  const [name, setName] = useState("");
  const [shared, setShared] = useState(false);
  const [saving, setSaving] = useState(false);
  const ready = name.trim().length > 0 && name.trim().length <= 80 && query.trim() !== "";

  return (
    <form
      className="cn-menu-form"
      onSubmit={(event) => {
        event.preventDefault();

        if (!ready) {
          return;
        }

        setSaving(true);
        void onSave({ name: name.trim(), query: query.trim(), shared }).then((saved) => {
          setSaving(false);

          if (saved) {
            close();
          }
        });
      }}
    >
      {query.trim() === "" && <p className="cn-menu-note">Run a query first; a view keeps its query.</p>}
      <label>
        Name
        <input value={name} maxLength={80} onChange={(event) => setName(event.target.value)} />
      </label>
      <label className="cn-check">
        <input type="checkbox" checked={shared} onChange={(event) => setShared(event.target.checked)} />
        Share with everyone
      </label>
      <button type="submit" className="cn-button primary" disabled={!ready || saving}>
        {saving ? "Saving…" : "Save"}
      </button>
    </form>
  );
}

/** The volume over the window, stacked by level. */
function Volume({ summary }: { summary: LogSummary | null }) {
  const buckets = summary?.buckets ?? [];
  const count = (bucket: LogSummary["buckets"][number], levels: Severity[]) =>
    levels.reduce((sum, level) => sum + (bucket.levels?.[level] ?? 0), 0);
  const hasLevels = buckets.some((bucket) => bucket.levels);
  const label = (index: number) => (buckets[index] ? formatClock(buckets[index].start) : "");

  return (
    <section className="cn-card" aria-labelledby="cn-logs-volume">
      <header>
        <h2 id="cn-logs-volume">Log volume</h2>
        <HelpTip label="Log volume">
          How many matching records arrived in each step of the top bar&apos;s range, stacked by level: debug, info,
          warning, and error with fatal on top. The server counts them.
        </HelpTip>
        <span className="cn-meta">
          {summary ? `${formatCount(summary.total)} events · per ${stepWords(summary.bucketSeconds)}` : "Counting…"}
        </span>
      </header>
      {buckets.length > 0 && (
        <div className="cn-logs-bars">
          <StackedBars
            title="Log volume by level"
            steps={buckets.map((_, index) => label(index))}
            labels={[label(0), label(Math.floor(buckets.length / 2)), "now"]}
            layers={
              hasLevels
                ? [
                    { name: "Debug", color: "var(--faint)", values: buckets.map((b) => count(b, ["Trace", "Debug"])) },
                    {
                      name: "Info",
                      color: "var(--info-fg)",
                      values: buckets.map((b) => count(b, ["Information", "Unspecified"])),
                    },
                    { name: "Warning", color: "var(--warning-fg)", values: buckets.map((b) => count(b, ["Warning"])) },
                    {
                      name: "Error and fatal",
                      color: "var(--error-fg)",
                      values: buckets.map((b) => count(b, ["Error", "Fatal"])),
                    },
                  ]
                : [
                    { name: "Below error", color: "var(--info-fg)", values: buckets.map((b) => b.total - b.errors) },
                    { name: "Error and fatal", color: "var(--error-fg)", values: buckets.map((b) => b.errors) },
                  ]
            }
          />
        </div>
      )}
    </section>
  );
}

/** The step of the volume chart in words. */
function stepWords(seconds: number): string {
  return seconds % 3600 === 0 ? `${seconds / 3600} h` : seconds % 60 === 0 ? `${seconds / 60} min` : `${seconds} s`;
}

/** The totals of the window per level; a click filters by that level. */
function ByLevel({ summary, onPick }: { summary: LogSummary | null; onPick(level: Severity): void }) {
  const counts = pickableLevels.map((level) => [level, summary?.levels?.[level] ?? 0] as const);
  const most = Math.max(1, ...counts.map(([, count]) => count));

  return (
    <section className="cn-card" aria-labelledby="cn-logs-levels">
      <header>
        <h2 id="cn-logs-levels">By level</h2>
        <HelpTip label="By level">
          How many matching records of the range each level has, counted by the server. Choose a level to show only its
          records.
        </HelpTip>
        <span className="cn-meta">Choose one to filter</span>
      </header>
      <div className="cn-levels">
        {counts.map(([level, count]) => (
          <button key={level} type="button" className="cn-level-row" onClick={() => onPick(level)}>
            <span>
              <b>{levelName[level]}</b>
              <span className="mono">{summary?.levels ? shortCount(count) : "—"}</span>
            </span>
            <span className="cn-level-bar" aria-hidden="true">
              <i
                style={{
                  width: `${Math.max(count > 0 ? 0.8 : 0, (count / most) * 100)}%`,
                  background: levelColor[level],
                }}
              />
            </span>
          </button>
        ))}
      </div>
    </section>
  );
}

/** Props for {@link Events}. */
interface EventsProps {
  items: LogEntry[];
  chosenId: number | null;
  total: number | null;
  loading: boolean;
  hasMore: boolean;
  loadingMore: boolean;
  aroundNote: string;
  onChoose(id: number): void;
  onMore(): void;
}

/** The matching records, newest first, a page at a time from the server. */
function Events({ items, chosenId, total, loading, hasMore, loadingMore, aroundNote, onChoose, onMore }: EventsProps) {
  return (
    <section className="cn-card cn-events" aria-labelledby="cn-logs-events">
      <header>
        <h2 id="cn-logs-events">Events</h2>
        <HelpTip label="Events">
          The matching records, newest first. Choose one to see everything it carries. Pages and their order come from
          the server.
        </HelpTip>
        <span className="cn-meta">
          {total === null
            ? aroundNote
            : `${items.length.toLocaleString("en")} of ${formatCount(total)} · ${aroundNote}`}
        </span>
      </header>
      {items.length === 0 ? (
        <p className="cn-empty">{loading ? "Searching…" : "No record matches this query in the range."}</p>
      ) : (
        <div className="cn-table-scroll">
          <table className="cn-table cn-events-table">
            <thead>
              <tr>
                <th scope="col">Time ↓</th>
                <th scope="col">Level</th>
                <th scope="col">Service</th>
                <th scope="col">Message</th>
                <th scope="col" className="wide advanced-only">
                  Host
                </th>
                <th scope="col" className="wide advanced-only">
                  Trace
                </th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr
                  key={item.id}
                  aria-selected={item.id === chosenId}
                  className={item.id === chosenId ? "chosen" : undefined}
                  onClick={() => onChoose(item.id)}
                >
                  <td className="mono">
                    <button type="button" className="cn-row-pick" onClick={() => onChoose(item.id)}>
                      {formatTime(item.timestamp)}
                    </button>
                  </td>
                  <td>
                    <span className={`cn-level ${item.severity.toLowerCase()}`}>{levelName[item.severity]}</span>
                  </td>
                  <td>{item.serviceName}</td>
                  <td className="cn-events-message">{item.body}</td>
                  <td className="wide advanced-only">{item.hostName ?? item.resourceAttributes["host.name"] ?? "—"}</td>
                  <td className="wide mono advanced-only">{item.traceId ? item.traceId.slice(0, 8) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <footer>
        <span>Pages come from the server</span>
        {hasMore && (
          <button type="button" className="link-button" onClick={onMore} disabled={loadingMore}>
            {loadingMore ? "Loading…" : "Load older →"}
          </button>
        )}
      </footer>
    </section>
  );
}
