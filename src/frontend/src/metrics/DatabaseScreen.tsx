import { useParams, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { AlertListItem, BlockHealth, ChartLine, DatabaseMetrics, DatabasePart, HiddenRows } from "../api/types";
import { engineName } from "../alerts/ruleWords";
import { ScreenBar } from "../ui/ScreenBar";
import { useViewMode } from "../ui/ViewToggle";
import { BlockPage } from "./BlockPage";
import { BlockStatus } from "./BlockStatus";
import { LineChart } from "./LineChart";
import { DatabaseIcon } from "./MetricCards";
import { formatAgo, formatAmount, formatRate, formatSeconds, formatSpan } from "./metricFormat";
import { rangeOf, rangeSearch, rangeWords, withRange } from "./metricRange";
import { RangePicker } from "./RangePicker";
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
import { blindingGaps, PermissionNotice } from "./database/PermissionNotice";
import { platformNote, tabsLeftOut } from "./database/platforms";
import { isPostgres } from "./database/postgres";
import { PostgresDatabases, PostgresOverview } from "./database/PostgresTabs";
import { PanelHead, TileName } from "./database/helpNames";
import type { AreaId } from "./database/areas";
import { databaseRowId } from "./database/drill";
import { OverviewTab } from "./database/OverviewTab";
import { ServerSwitcher } from "./database/ServerSwitcher";
import { tabsOf, usesRange } from "./database/tabs";
import { useAreaStates } from "./database/useAreaStates";
import { useOpenAlerts } from "./database/useOpenAlerts";
import { ServerAlerts, SystemHidden, SystemTag, SystemToggle, useSystemShown } from "./database/SystemFilter";
import "./database/live.css";
import "./database/screen.css";
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
 * One database server over the chosen range (ADR 0018). It opens on its Overview, each area as weather from the
 * server's open alerts, which leads to the area's tab (VEY-62). Simple shows how it is, the numbers that say whether it
 * is fine, its databases with their size and last backup, its SQL Agent jobs, and the queries running long now;
 * Advanced adds page life, the waits, the server over time, growth, I/O latency, and the top queries (VEY-37).
 * A PostgreSQL server has its own Overview and Databases tabs instead (VEY-33). Reached from its card on the Metrics
 * overview, from its alerts, and from any other server through the switcher in the screen's bar.
 */
export function DatabaseScreen() {
  const { engine = "", server = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeOf(params);
  const [mode, setMode] = useViewMode();
  const advanced = mode === "advanced";
  const postgres = isPostgres(engine);
  // What the monitoring login may not read, and on which platform: the agent checks every 5 minutes (VEY-58, VEY-57).
  // No agent reads a PostgreSQL server, so it is not asked.
  const { data: permissions } = useMetricData(
    `permissions-${engine}-${server}`,
    (signal) => (postgres ? Promise.resolve(null) : api.databasePermissions(engine, server, signal)),
    { refreshMs: 60_000 },
  );
  const leftOut = tabsLeftOut(permissions?.platform);
  const tabs = tabsOf(engine, advanced).filter((one) => postgres || !leftOut?.tabs.includes(one.id));
  const first = tabs[0]?.id ?? "overview";
  const tab = tabs.find((one) => one.id === params.get("tab"))?.id ?? first;
  const note = platformNote(permissions?.platform, tab);
  const [systemShown] = useSystemShown();
  // The server leaves out its own databases, jobs, statements, and tables unless System is on (VEY-66).
  const { data, notFound, error } = useMetricData(
    `database-${engine}-${server}-${range}`,
    (signal) => api.databaseMetrics(engine, server, range, systemShown, signal),
    { choices: String(systemShown) },
  );
  const blind = blindingGaps(permissions, tab).length > 0;
  // Read once for the banner, the overview, and the switcher, so they never disagree (VEY-62).
  const { alerts } = useOpenAlerts("database", `${engine} ${server}`);
  const health = healthFrom(alerts);
  const areaStates = useAreaStates(
    `${engine} ${server}`,
    tabs.filter((one) => one.id !== "overview").map((one) => one.id as AreaId),
    postgres || tab !== "overview",
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
      <ScreenBar
        trail={[
          { label: "Metrics", to: `/metrics?${rangeSearch(range)}` },
          { label: "Database servers", to: `/metrics?${rangeSearch(range)}#metrics-databases` },
          {
            label: (
              <ServerSwitcher
                engine={engine}
                server={server}
                health={health}
                tab={tab}
                range={range}
                advanced={advanced}
                card={data?.card ?? null}
              />
            ),
          },
        ]}
        view
      >
        {usesRange(tab) && (
          <RangePicker
            value={range}
            onChange={(next) => setParams((current) => withRange(current, next), { replace: true })}
          />
        )}
        <SystemToggle />
      </ScreenBar>
      <ServerAlerts.Provider value={alerts}>
        <BlockPage kind="database" name={`${engine} ${server}`} alerts={alerts}>
          <div className="metrics-detail-head">
            <span className="metric-icon large database">
              <DatabaseIcon size={26} />
            </span>
            <div>
              <div className="db-title">
                <h1>{server}</h1>
                <ServerWeather alerts={alerts} />
              </div>
              <Tags engine={engine} database={data} />
            </div>
          </div>
          <nav className="database-tabs" aria-label="Server views">
            {tabs.map((one) => (
              <button
                key={one.id}
                type="button"
                aria-current={one.id === tab ? "page" : undefined}
                onClick={() => go({ tab: one.id === first ? null : one.id })}
              >
                {one.label}
              </button>
            ))}
          </nav>
          {data && (
            <BlockStatus
              kind="database"
              name={`${engine} ${server}`}
              label={`${engineName(engine)} ${server}`}
              alerts={alerts}
            />
          )}

          {error && (
            <p className="metrics-error" role="alert">
              {data ? `Could not refresh: ${error}.` : `The server could not be loaded: ${error}`}
            </p>
          )}
          {leftOut && <p className="metrics-empty">{leftOut.reason}</p>}
          {note && <p className="metrics-empty">{note}</p>}
          <PermissionNotice permissions={permissions} tab={tab} server={server} />
          {!postgres && tab === "overview" && (
            <OverviewTab database={data} states={areaStates} range={range} onOpen={(area) => go({ tab: area })} />
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
              onMoment={(at) => go({ tab: "live", at })}
            />
          )}
          {tab === "jobs" && <JobsTab engine={engine} server={server} advanced={advanced} blind={blind} />}
          {tab === "events" && <EventsTab engine={engine} server={server} range={range} advanced={advanced} />}
          {tab === "server" && (
            <ServerTab engine={engine} server={server} range={range} advanced={advanced} blind={blind} />
          )}
          {tab !== "live" && notFound && (
            <p className="metrics-empty">
              {server} sent no metrics {rangeWords(range, "within")}.
            </p>
          )}
          {data && postgres && tab === "overview" && (
            <PostgresOverview database={data} advanced={advanced} onDatabases={() => go({ tab: "databases" })} />
          )}
          {data && postgres && tab === "databases" && <PostgresDatabases database={data} advanced={advanced} />}
          {data && tab === "waits" && (
            <Numbers database={data} advanced={advanced} onTab={(next) => go({ tab: next, at: null })} />
          )}
          {data && !postgres && (tab === "waits" || tab === "databases") && (
            <div className="metric-panels">
              {tab === "waits" && <Waits database={data} />}
              {tab === "waits" && <Activity database={data} />}
              {tab === "waits" && <WaitTypes engine={engine} server={server} range={range} advanced={advanced} />}
              {tab === "databases" && (
                <Databases databases={data.databases} hidden={data.hiddenDatabases} advanced={advanced} />
              )}
              {tab === "databases" && <StorageTab engine={engine} server={server} advanced={advanced} blind={blind} />}
              {tab === "databases" && (
                <IndexesSection engine={engine} server={server} advanced={advanced} blind={blind} />
              )}
            </div>
          )}
        </BlockPage>
      </ServerAlerts.Provider>
    </main>
  );
}

/**
 * The server's health from its open alerts, as its weather on the home (ADR 0007).
 *
 * @param alerts - Its open alerts, or null when unknown.
 * @returns Warning or Caution from the worst, Ok without any, or null when unknown.
 */
function healthFrom(alerts: AlertListItem[] | null): BlockHealth | null {
  if (alerts === null) {
    return null;
  }

  // The server sends them the worst first (VEY-68).
  return alerts[0]?.severity ?? "Ok";
}

/** The server's weather beside its name: a storm or showers and how many alerts are open; nothing when fine. */
function ServerWeather({ alerts }: { alerts: AlertListItem[] | null }) {
  const health = healthFrom(alerts);

  if (alerts === null || alerts.length === 0 || health === null) {
    return null;
  }

  return (
    <span className={`db-weather ${health.toLowerCase()}`}>
      {health === "Warning" ? "storm" : "showers"}: {alerts.length} {alerts.length === 1 ? "alert" : "alerts"} open
    </span>
  );
}

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
  /**
   * Opens another tab, where a figure leads (VEY-62).
   *
   * @param tab - The tab, by its name in the address.
   */
  onTab: (tab: string) => void;
}

/**
 * The numbers that say whether the server is fine, each with its line over the range. Blocked sessions and deadlocks
 * lead to the Blocking tab and the longest query to the Live tab, when there is something to see (VEY-62).
 */
function Numbers({ database, advanced, onTab }: NumbersProps) {
  const { card } = database;
  const blocked = card.blocked !== null && card.blocked > 0;
  const deadlocks = card.deadlocks !== null && card.deadlocks > 0;
  const running = card.longestQuerySeconds !== null && card.longestQuerySeconds > 0;

  return (
    <section className="database-tiles" aria-label="Now">
      <Tile
        label="Batch requests"
        help={
          "SQL Server's Batch Requests/sec counter: batches of statements received a " +
          "second, read every 15 seconds; the line covers the range. It is the workload. " +
          "Read it against this server's usual for the hour; a sudden drop can mean the " +
          "applications cannot reach it."
        }
        value={formatRate(card.batchRequests)}
        unit="/s"
        line={database.requests}
      />
      <Tile
        label="Connections"
        help={
          "SQL Server's User Connections counter, read every 15 seconds. A steady number is " +
          "normal. A climb that never comes down often means an application leaks " +
          "connections or its pool keeps growing."
        }
        value={whole(card.connections)}
        line={database.connections}
      />
      <Tile
        label="Blocked"
        help={
          "SQL Server's Processes blocked counter: sessions waiting for a lock another " +
          "session holds, read every 15 seconds. 0 is normal. The blocking rule raises an " +
          "alert when sessions stay blocked for 5 minutes; the Live tab shows who holds " +
          "them up."
        }
        value={whole(card.blocked)}
        unit="sessions"
        line={database.blocked}
        tone={blocked ? "warn" : undefined}
        onOpen={blocked ? () => onTab("blocking") : undefined}
        where="See it on Blocking"
      />
      <Tile
        label="Deadlocks"
        help={
          "Deadlocks in the last 15 minutes, from SQL Server's Number of Deadlocks/sec " +
          "counter. In a deadlock SQL Server ends one of the sessions, and its application " +
          "gets error 1205. Any is worth a look on the Blocking tab."
        }
        value={whole(card.deadlocks)}
        unit="in 15 min"
        line={database.deadlocks}
        tone={deadlocks ? "warn" : undefined}
        onOpen={deadlocks ? () => onTab("blocking") : undefined}
        where="See them on Blocking"
      />
      {advanced && (
        <Tile
          label="Page life"
          help={
            "Page life expectancy: how many seconds a data page stays in memory, from SQL " +
            "Server's counter, read every 15 seconds. Higher is better. A sharp fall means " +
            "queries read more than memory holds, often a scan that an index would avoid."
          }
          value={whole(card.pageLifeSeconds)}
          unit="s"
          line={database.pageLife}
        />
      )}
      <Tile
        label="Longest query"
        help={
          "The longest running query in any database now, from sys.dm_exec_requests, read " +
          "every minute. Amber from 5 minutes; the long query rule raises a caution from 5 " +
          "minutes and a warning from 30."
        }
        value={card.longestQuerySeconds === null ? "–" : formatClock(card.longestQuerySeconds)}
        unit={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 60 ? "min" : "s"}
        line={database.longestQuery}
        tone={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 300 ? "warn" : undefined}
        onOpen={running ? () => onTab("live") : undefined}
        where="See it live"
      />
    </section>
  );
}

/** Props for {@link Tile}. */
interface TileProps {
  /** What the number is. */
  label: string;
  /** What the number means, behind the question mark beside its name (VEY-60). */
  help: string;
  /** The number now, formatted. */
  value: string;
  /** The unit after it. */
  unit?: string;
  /** The number over the range. */
  line: ChartLine;
  /** `warn` when the number needs a look. */
  tone?: "warn";
  /** Opens where the number leads; without it, the tile is not a button. */
  onOpen?: () => void;
  /** Where it leads, in a few words, shown under the line. */
  where?: string;
}

/**
 * One number with its line under it, and a question mark that explains it. A tile that leads somewhere lays a button
 * over itself, under its name's row, since a button cannot hold the question mark's, and says where (VEY-62).
 */
function Tile({ label, help, value, unit, line, tone, onOpen, where }: TileProps) {
  const className = ["database-tile", tone, onOpen ? "opens" : null].filter(Boolean).join(" ");

  return (
    <div className={className}>
      <TileName name={label} help={help} />
      <b>
        {value}
        {unit && <small> {unit}</small>}
      </b>
      <Sparkline line={line} color={tone ? "var(--warning-fg)" : "var(--accent)"} />
      {onOpen && where && (
        <span className="db-tile-where" aria-hidden="true">
          {where} ›
        </span>
      )}
      {onOpen && (
        <button
          type="button"
          className="live-tile-open"
          onClick={onOpen}
          aria-label={`${label}: ${value}. ${where ?? "Open"}`}
        >
          <span className="live-tile-go" aria-hidden="true">
            ›
          </span>
        </button>
      )}
    </div>
  );
}

/** What sessions waited on, per category, over the range. Advanced only. */
function Waits({ database }: { database: DatabaseMetrics }) {
  return (
    <section className="metric-panel" aria-labelledby="database-waits">
      <PanelHead
        id="database-waits"
        name="Waits"
        help={
          "How many seconds sessions spent waiting each second, by category, from " +
          "sys.dm_os_wait_stats, read every 15 seconds. Some waiting is normal; watch the " +
          "category that grows. Lock means blocking, Disk I/O slow storage or too many " +
          "reads, CPU a busy processor."
        }
      >
        <span>seconds waited per second, by category</span>
      </PanelHead>
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
      <PanelHead
        id="database-activity"
        name="Sessions"
        help={
          "User connections and blocked sessions over the range, from SQL Server's User " +
          "Connections and Processes blocked counters, read every 15 seconds. Blocked " +
          "sessions that rise with connections mean requests pile up behind one that holds " +
          "a lock."
        }
      >
        <span>connections and blocked sessions</span>
      </PanelHead>
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
  /** The server's databases, the largest first; its own only when System is on. */
  databases: DatabasePart[];
  /** The server's own databases the server left out. */
  hidden: HiddenRows;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** Each database with its size and last full backup; Advanced adds growth, I/O latency, and its longest query. */
function Databases({ databases, hidden, advanced }: DatabasesProps) {
  const listed = databases;

  return (
    <section className="metric-panel" aria-labelledby="database-databases">
      <PanelHead
        id="database-databases"
        name="Databases"
        help={
          "Each database with the size of its files (sys.master_files) and the age of its " +
          "last full backup (msdb's backup history), read every minute. A backup reads as " +
          "late from 26 hours, the backup rule's caution. Advanced adds growth a day and " +
          "the average I/O latency of its files, read every 15 seconds; a rise means " +
          "storage slows the queries down."
        }
      >
        <span>{listed.length === 1 ? "1 database" : `${listed.length} databases`}</span>
      </PanelHead>
      {listed.length === 0 ? (
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
            {listed.map((database) => (
              <tr key={database.name} id={databaseRowId(database.name)} className="db-drill-target" tabIndex={-1}>
                <th scope="row">
                  {database.name}
                  {database.system && <SystemTag />}
                </th>
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
      <SystemHidden hidden={hidden} noun="database" />
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
