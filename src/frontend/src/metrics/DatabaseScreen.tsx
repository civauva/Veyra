import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { ChartLine, DatabaseMetrics, DatabasePart } from "../api/types";
import { engineName } from "../alerts/ruleWords";
import { useViewMode, ViewToggle } from "../app/ViewToggle";
import { BlockStatus } from "./BlockStatus";
import { LineChart } from "./LineChart";
import { DatabaseIcon } from "./MetricCards";
import { formatAgo, formatAmount, formatRate, formatSeconds, formatSpan } from "./metricFormat";
import { RangePicker, rangeFrom } from "./RangePicker";
import { Sparkline } from "./Sparkline";
import { useMetricData } from "./useMetricData";
import { LiveTab } from "./database/LiveTab";
import { QueriesTab } from "./database/QueriesTab";
import { BlockingTab } from "./database/BlockingTab";
import { StorageTab } from "./database/StorageTab";
import { IndexesSection } from "./database/IndexesSection";
import { ServerTab, WaitTypes } from "./database/ServerTab";
import { JobsTab } from "./database/JobsTab";
import { EventsTab } from "./database/EventsTab";
import "./database/live.css";
import "./metrics.css";

/** A backup older than this many hours reads as late, as the backup-age rule's default caution. */
const lateBackupHours = 26;

/** The wait categories' colours, in the order the server reports them, largest first. */
const waitColors = [
  "var(--accent)",
  "var(--error-fg)",
  "var(--ok-fg)",
  "var(--metric-violet)",
  "var(--warning-fg)",
  "var(--faint)",
];

/**
 * One database server over the chosen range (ADR 0018). Simple shows how it is, the numbers that say whether it is
 * fine, its databases with their size and last backup, its SQL Agent jobs, and the queries running long now;
 * Advanced adds page life, the waits, the server over time, growth, I/O latency, and the top queries (VEY-37).
 * Reached from its card on the Metrics overview, and from its alerts.
 */
export function DatabaseScreen() {
  const { engine = "", server = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeFrom(params.get("range"));
  const [mode, setMode] = useViewMode();
  const advanced = mode === "advanced";
  const tabs = advanced ? allTabs : simpleTabs;
  const tab = tabs.find((one) => one.id === params.get("tab"))?.id ?? "live";
  const { data, notFound, error } = useMetricData(`database-${engine}-${server}-${range}`, (signal) =>
    api.databaseMetrics(engine, server, range, signal),
  );

  function go(next: Record<string, string | null>) {
    setParams(
      (current) => {
        const params = new URLSearchParams(current);
        Object.entries(next).forEach(([key, value]) => (value === null ? params.delete(key) : params.set(key, value)));

        return params;
      },
      { replace: true },
    );
  }

  return (
    <main className="metrics">
      <Link className="metrics-back" to={`/metrics?range=${range}`}>
        ← Metrics
      </Link>
      <div className="metrics-detail-head">
        <span className="metric-icon large database">
          <DatabaseIcon size={26} />
        </span>
        <div>
          <h1>{server}</h1>
          <Tags engine={engine} database={data} />
        </div>
        <ViewToggle />
        {tab !== "live" && <RangePicker value={range} onChange={(next) => go({ range: next })} />}
      </div>
      <nav className="database-tabs" aria-label="Server views">
        {tabs.map((one) => (
          <button
            key={one.id}
            type="button"
            aria-current={one.id === tab ? "page" : undefined}
            onClick={() => go({ tab: one.id === "live" ? null : one.id })}
          >
            {one.label}
          </button>
        ))}
      </nav>
      {data && <BlockStatus kind="database" name={`${engine} ${server}`} label={`${engineName(engine)} ${server}`} />}

      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `The server could not be loaded: ${error}`}
        </p>
      )}
      {tab === "live" && (
        <LiveTab
          engine={engine}
          server={server}
          at={params.get("at")}
          advanced={advanced}
          onNow={() => go({ at: null })}
          onAdvanced={() => setMode("advanced")}
        />
      )}
      {tab === "queries" && <QueriesTab engine={engine} server={server} range={range} advanced={advanced} />}
      {tab === "blocking" && (
        <BlockingTab
          engine={engine}
          server={server}
          range={range}
          advanced={advanced}
          onMoment={(at) => go({ tab: null, at })}
        />
      )}
      {tab === "jobs" && <JobsTab engine={engine} server={server} advanced={advanced} />}
      {tab === "events" && <EventsTab engine={engine} server={server} range={range} advanced={advanced} />}
      {tab === "server" && <ServerTab engine={engine} server={server} range={range} advanced={advanced} />}
      {tab !== "live" && notFound && (
        <p className="metrics-empty">
          {server} sent no metrics within the last {range}.
        </p>
      )}
      {data && tab === "waits" && <Numbers database={data} advanced={advanced} />}
      {data && tab !== "live" && (
        <div className="metric-panels">
          {tab === "waits" && <Waits database={data} />}
          {tab === "waits" && <Activity database={data} />}
          {tab === "waits" && <WaitTypes engine={engine} server={server} range={range} advanced={advanced} />}
          {tab === "databases" && <Databases databases={data.databases} advanced={advanced} />}
          {tab === "databases" && <StorageTab engine={engine} server={server} advanced={advanced} />}
          {tab === "databases" && <IndexesSection engine={engine} server={server} advanced={advanced} />}
        </div>
      )}
    </main>
  );
}

/** A tab of the database screen (ADR 0020). */
interface DatabaseTab {
  /** Its name in the address. */
  id: string;
  /** Its name on screen. */
  label: string;
}

/** The tabs of the Simple view: what an application owner needs. */
const simpleTabs: DatabaseTab[] = [
  { id: "live", label: "Live" },
  { id: "queries", label: "Queries" },
  { id: "blocking", label: "Blocking" },
  { id: "databases", label: "Databases" },
  { id: "jobs", label: "Jobs" },
  { id: "events", label: "Events" },
  { id: "server", label: "Server" },
];

/** The tabs of the Advanced view, for a DBA. */
const allTabs: DatabaseTab[] = [
  { id: "live", label: "Live" },
  { id: "queries", label: "Queries" },
  { id: "waits", label: "Waits" },
  { id: "blocking", label: "Blocking" },
  { id: "databases", label: "Databases" },
  { id: "jobs", label: "Jobs" },
  { id: "events", label: "Events" },
  { id: "server", label: "Server" },
];

/** Props for {@link Tags}. */
interface TagsProps {
  /** The engine from the address. */
  engine: string;
  /** The screen's data, once loaded. */
  database: DatabaseMetrics | null;
}

/** The engine, the environment, the machine, and how fresh the data is. */
function Tags({ engine, database }: TagsProps) {
  const card = database?.card;
  const machine = [
    card?.cpus ? `${card.cpus} CPUs` : null,
    card?.memoryBytes ? `${formatAmount(card.memoryBytes)} in use` : null,
  ]
    .filter(Boolean)
    .join(" · ");
  const tags = [
    engineName(engine),
    card?.environment,
    machine || null,
    card?.uptimeSeconds ? `up ${formatSpan(card.uptimeSeconds)}` : null,
    card ? `last point ${formatAgo(card.lastSeen, new Date())}` : null,
  ].filter((tag): tag is string => Boolean(tag));

  return (
    <ul className="metric-tags">
      {tags.map((tag) => (
        <li key={tag}>{tag}</li>
      ))}
    </ul>
  );
}

/** Props for {@link Numbers}. */
interface NumbersProps {
  /** The server. */
  database: DatabaseMetrics;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** The numbers that say whether the server is fine, each with its line over the range. */
function Numbers({ database, advanced }: NumbersProps) {
  const { card } = database;

  return (
    <section className="database-tiles" aria-label="Now">
      <Tile label="Batch requests" value={formatRate(card.batchRequests)} unit="/s" line={database.requests} />
      <Tile label="Connections" value={whole(card.connections)} line={database.connections} />
      <Tile
        label="Blocked"
        value={whole(card.blocked)}
        unit="sessions"
        line={database.blocked}
        tone={card.blocked !== null && card.blocked > 0 ? "warn" : undefined}
      />
      <Tile
        label="Deadlocks"
        value={whole(card.deadlocks)}
        unit="in 15 min"
        line={database.deadlocks}
        tone={card.deadlocks !== null && card.deadlocks > 0 ? "warn" : undefined}
      />
      {advanced && <Tile label="Page life" value={whole(card.pageLifeSeconds)} unit="s" line={database.pageLife} />}
      <Tile
        label="Longest query"
        value={card.longestQuerySeconds === null ? "–" : formatClock(card.longestQuerySeconds)}
        unit={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 60 ? "min" : "s"}
        line={database.longestQuery}
        tone={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 300 ? "warn" : undefined}
      />
    </section>
  );
}

/** Props for {@link Tile}. */
interface TileProps {
  /** What the number is. */
  label: string;
  /** The number now, formatted. */
  value: string;
  /** The unit after it. */
  unit?: string;
  /** The number over the range. */
  line: ChartLine;
  /** `warn` when the number needs a look. */
  tone?: "warn";
}

/** One number with its line under it. */
function Tile({ label, value, unit, line, tone }: TileProps) {
  return (
    <div className={tone ? `database-tile ${tone}` : "database-tile"}>
      <span>{label}</span>
      <b>
        {value}
        {unit && <small> {unit}</small>}
      </b>
      <Sparkline line={line} color={tone ? "var(--warning-fg)" : "var(--accent)"} />
    </div>
  );
}

/** What sessions waited on, per category, over the range. Advanced only. */
function Waits({ database }: { database: DatabaseMetrics }) {
  return (
    <section className="metric-panel" aria-labelledby="database-waits">
      <div className="metric-panel-head">
        <h2 id="database-waits">Waits</h2>
        <span>seconds waited per second, by category</span>
      </div>
      {database.waits.length === 0 ? (
        <p className="metrics-empty">No waits within the range.</p>
      ) : (
        <LineChart
          title="Waits"
          format={(value) => formatRate(value)}
          series={database.waits.map((line, index) => ({
            line,
            color: waitColors[index % waitColors.length] ?? "var(--accent)",
            name: line.label,
            filled: index === 0,
          }))}
        />
      )}
    </section>
  );
}

/** Connections, blocked sessions, and the longest query over the range. Advanced only. */
function Activity({ database }: { database: DatabaseMetrics }) {
  return (
    <section className="metric-panel" aria-labelledby="database-activity">
      <div className="metric-panel-head">
        <h2 id="database-activity">Sessions</h2>
        <span>connections and blocked sessions</span>
      </div>
      <LineChart
        title="Sessions"
        format={(value) => whole(value)}
        series={[
          { line: database.connections, color: "var(--accent)", name: "Connections", filled: true },
          { line: database.blocked, color: "var(--warning-fg)", name: "Blocked" },
        ]}
      />
    </section>
  );
}

/** Props for {@link Databases}. */
interface DatabasesProps {
  /** The server's databases, the largest first. */
  databases: DatabasePart[];
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** Each database with its size and last full backup; Advanced adds growth, I/O latency, and its longest query. */
function Databases({ databases, advanced }: DatabasesProps) {
  return (
    <section className="metric-panel" aria-labelledby="database-databases">
      <div className="metric-panel-head">
        <h2 id="database-databases">Databases</h2>
        <span>{databases.length === 1 ? "1 database" : `${databases.length} databases`}</span>
      </div>
      {databases.length === 0 ? (
        <p className="metrics-empty">The server reports no databases.</p>
      ) : (
        <table className="route-table">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col" className="number">
                Size
              </th>
              {advanced && (
                <th scope="col" className="number">
                  Growth / day
                </th>
              )}
              <th scope="col">Last full backup</th>
              {advanced && (
                <th scope="col" className="number">
                  I/O latency
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {databases.map((database) => (
              <tr key={database.name}>
                <th scope="row">{database.name}</th>
                <td data-label="Size" className="number">
                  {database.sizeBytes === null ? "–" : formatAmount(database.sizeBytes)}
                </td>
                {advanced && (
                  <td data-label="Growth / day" className="number">
                    {database.growthBytesPerDay === null ? "–" : `+${formatAmount(database.growthBytesPerDay)}`}
                  </td>
                )}
                <td data-label="Last full backup">
                  <Backup database={database} />
                </td>
                {advanced && (
                  <td data-label="I/O latency" className="number">
                    {database.ioLatencySeconds === null ? "–" : formatSeconds(database.ioLatencySeconds)}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

/** The age of a database's last full backup, late from the backup-age rule's default caution. */
function Backup({ database }: { database: DatabasePart }) {
  if (database.backupAgeSeconds === null) {
    return <span className="database-badge none">{database.name === "tempdb" ? "not backed up" : "never"}</span>;
  }

  const late = database.backupAgeSeconds >= lateBackupHours * 3600;

  return (
    <span className={late ? "database-badge late" : "database-badge ok"}>
      {formatSpan(database.backupAgeSeconds)} ago
    </span>
  );
}

/** A whole number, or a dash. */
function whole(value: number | null): string {
  return value === null ? "–" : Math.round(value).toLocaleString("en-US");
}

/** Seconds as a clock, `2:14` for two minutes, `1:02:14` from an hour; under a minute, the seconds alone. */
function formatClock(seconds: number): string {
  const total = Math.round(seconds);

  if (total < 60) {
    return String(total);
  }

  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = String(total % 60).padStart(2, "0");

  return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${rest}` : `${minutes}:${rest}`;
}
