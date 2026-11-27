import type { ChartLine, DatabaseMetrics, DatabaseTable, HiddenRows } from "../../api/types";
import { HelpTip } from "../../ui/HelpTip";
import { LineChart } from "../LineChart";
import { formatAmount, formatRate, formatShare } from "../metricFormat";
import { Ring } from "../Ring";
import { Sparkline } from "../Sparkline";
import { PanelHead, TileName } from "./helpNames";
import { connectionsRatio, connectionsTone, formatRows, wraparoundTone } from "./postgres";
import { SystemHidden, SystemTag } from "./SystemFilter";
import "./postgres.css";

/** Props for {@link PostgresOverview} and {@link PostgresDatabases}. */
export interface PostgresTabProps {
  /** The server's screen, from `GET /api/v1/metrics/databases/postgresql/{server}`. */
  database: DatabaseMetrics;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** Props for {@link PostgresOverview}. */
export interface PostgresOverviewProps extends PostgresTabProps {
  /** Opens the Databases tab, where the database closest to wraparound leads (VEY-62). */
  onDatabases?: () => void;
}

/**
 * A PostgreSQL server's Overview tab (VEY-33): its connections of the maximum and the database closest to transaction
 * ID wraparound as rings, then blocked sessions, deadlocks, the longest running query, and transactions now. The
 * Advanced view adds connections against the maximum and transactions over the range. The wraparound ring leads to
 * the databases, each with its own age (VEY-62).
 */
export function PostgresOverview({ database, advanced, onDatabases }: PostgresOverviewProps) {
  const { card } = database;
  const ratio = connectionsRatio(card.connections, card.maxConnections);
  // The server picks the database oldest to wraparound (VEY-68).
  const oldest = database.overview.oldestWraparound;

  return (
    <>
      <section className="metric-panel pg-now" aria-labelledby="pg-now">
        <PanelHead
          id="pg-now"
          name="Now"
          help={
            "The server at its latest read, every 30 seconds: connections and transaction ID " +
            "age as rings, then blocked sessions, deadlocks, the longest query, and " +
            "transactions a second, each with its line over the range. Amber marks where an " +
            "alert would open."
          }
        >
          <span>{card.databases === 1 ? "1 database" : `${card.databases} databases`}</span>
        </PanelHead>
        <div className="pg-now-body">
          <div className="pg-rings">
            <div className="pg-ring">
              <Ring
                share={ratio}
                label="Connections"
                detail={`${whole(card.connections)} of ${whole(card.maxConnections)}`}
                tone={connectionsTone(ratio)}
              />
              <span className="pg-ring-help">
                <HelpTip label="Connections">
                  Sessions open on the server, idle ones too, against its max_connections, from pg_stat_database, read
                  every 30 seconds. Amber from 80% and red from 90%: at 100% new connections are refused. A connection
                  pool in front of the applications keeps it low.
                </HelpTip>
              </span>
            </div>
            <div className="pg-ring">
              <Ring
                share={card.wraparoundRatio}
                label="Wraparound"
                detail={oldest ? `oldest in ${oldest.name}` : "not reported"}
                tone={wraparoundTone(card.wraparoundRatio)}
              />
              <span className="pg-ring-help">
                <HelpTip label="Wraparound">
                  How close the database nearest the limit is to transaction ID wraparound: age(datfrozenxid) against
                  about 2.1 billion, read every 30 seconds. Vacuum freezes old rows and lowers it. Amber from 50% and
                  red from 75%; near the limit PostgreSQL stops taking writes, so make sure autovacuum keeps up.
                </HelpTip>
              </span>
              {oldest && onDatabases && (
                <button
                  type="button"
                  className="link-button pg-ring-go"
                  aria-label={`See ${oldest.name} and the other databases`}
                  onClick={onDatabases}
                >
                  See the databases ›
                </button>
              )}
            </div>
          </div>
          <div className="database-tiles pg-tiles">
            <Figure
              label="Blocked"
              help={
                "Sessions that have waited over 30 seconds for a lock, from pg_locks, read every " +
                "30 seconds. 0 is normal. The usual cause is a session that holds a lock in a " +
                "long or idle transaction."
              }
              value={whole(card.blocked)}
              unit="sessions"
              line={database.blocked}
              warn={card.blocked !== null && card.blocked > 0}
            />
            <Figure
              label="Deadlocks"
              help={
                "Deadlocks in the last 15 minutes, from pg_stat_database. PostgreSQL ends one of " +
                "the two transactions with an error. Deadlocks that repeat usually mean two parts " +
                "of an application lock the same rows in a different order."
              }
              value={whole(card.deadlocks)}
              unit="in 15 min"
              line={database.deadlocks}
              warn={card.deadlocks !== null && card.deadlocks > 0}
            />
            <Figure
              label="Longest query"
              help={
                "The longest active query of the applications now, from pg_stat_activity, read " +
                "every 30 seconds; autovacuum and the agent's own session are left out. Amber " +
                "from 5 minutes."
              }
              value={card.longestQuerySeconds === null ? "–" : formatClock(card.longestQuerySeconds)}
              unit={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 60 ? "min" : "s"}
              line={database.longestQuery}
              warn={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 300}
            />
            <Figure
              label="Commits"
              help={
                "Transactions committed a second, from pg_stat_database, read every 30 seconds. " +
                "It is the server's workload; read it against its usual."
              }
              value={formatRate(card.commitsPerSecond)}
              unit="/s"
              line={database.commits ?? database.requests}
            />
            <Figure
              label="Rollbacks"
              help={
                "Transactions rolled back a second, from pg_stat_database, read every 30 seconds. " +
                "A few are normal. A rise means the applications run into errors, such as " +
                "deadlocks, timeouts, or broken constraints."
              }
              value={formatRate(card.rollbacksPerSecond)}
              unit="/s"
              line={database.rollbacks ?? database.requests}
            />
          </div>
        </div>
      </section>
      {advanced && (
        <div className="metric-panels">
          <section className="metric-panel" aria-labelledby="pg-connections">
            <PanelHead
              id="pg-connections"
              name="Connections"
              help={
                "Sessions open over the range, idle ones too, against max_connections, the dashed " +
                "line, read every 30 seconds. A line that keeps climbing toward the maximum means " +
                "connections leak or a pool is set too large."
              }
            >
              <span>in use against the maximum</span>
            </PanelHead>
            <LineChart
              title="Connections"
              format={whole}
              series={[
                { line: database.connections, color: "var(--accent)", name: "In use", filled: true },
                ...(database.maxConnections
                  ? [{ line: database.maxConnections, color: "var(--faint)", name: "Maximum", dashed: true }]
                  : []),
              ]}
            />
          </section>
          <section className="metric-panel" aria-labelledby="pg-transactions">
            <PanelHead
              id="pg-transactions"
              name="Transactions"
              help={
                "Transactions committed and rolled back a second over the range, from " +
                "pg_stat_database, read every 30 seconds. Rollbacks that rise while commits stay " +
                "level mean more of the applications' work fails."
              }
            >
              <span>committed and rolled back per second</span>
            </PanelHead>
            <LineChart
              title="Transactions"
              format={(value) => formatRate(value)}
              series={[
                { line: database.commits ?? database.requests, color: "var(--ok-fg)", name: "Commits", filled: true },
                ...(database.rollbacks
                  ? [{ line: database.rollbacks, color: "var(--error-fg)", name: "Rollbacks" }]
                  : []),
              ]}
            />
          </section>
        </div>
      )}
    </>
  );
}

/**
 * A PostgreSQL server's Databases tab (VEY-33): each database with its size and growth, how far its oldest
 * transaction ID is to wraparound, and its dead rows; then the tables with the most dead rows, which a vacuum
 * clears. The Advanced view adds connections per database, live rows, and vacuums of the last day.
 */
export function PostgresDatabases({ database, advanced }: PostgresTabProps) {
  // The server leaves out postgres and the templates unless System is on (VEY-64, VEY-66); the Overview's
  // wraparound still counts them.
  const listed = database.databases;

  return (
    <div className="metric-panels pg-panels">
      <section className="metric-panel" aria-labelledby="pg-databases">
        <PanelHead
          id="pg-databases"
          name="Databases"
          help={
            "Each database with its size and growth a day, its transaction ID age against the " +
            "wraparound limit, and its share of dead rows, read every 30 seconds. The age bar " +
            "turns amber from 50% and red from 75%. Advanced adds its sessions and manual " +
            "vacuums a day; autovacuum runs are not counted."
          }
        >
          <span>{listed.length === 1 ? "1 database" : `${listed.length} databases`}</span>
        </PanelHead>
        {listed.length === 0 ? (
          <p className="metrics-empty">The server reports no databases.</p>
        ) : (
          <table className="route-table pg-table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col" className="number">
                  Size
                </th>
                <th scope="col" className="number">
                  Growth / day
                </th>
                {advanced && (
                  <th scope="col" className="number">
                    Connections
                  </th>
                )}
                <th scope="col">Transaction ID age</th>
                <th scope="col" className="number">
                  Dead rows
                </th>
                {advanced && (
                  <th scope="col" className="number">
                    Manual vacuums / day
                  </th>
                )}
              </tr>
            </thead>
            <tbody>
              {listed.map((part) => (
                <tr key={part.name}>
                  <th scope="row">
                    {part.name}
                    {part.system && <SystemTag />}
                  </th>
                  <td data-label="Size" className="number">
                    {formatAmount(part.sizeBytes)}
                  </td>
                  <td data-label="Growth / day" className="number">
                    {part.growthBytesPerDay === null
                      ? "–"
                      : `${sign(part.growthBytesPerDay)}${formatAmount(Math.abs(part.growthBytesPerDay))}`}
                  </td>
                  {advanced && (
                    <td data-label="Connections" className="number">
                      {whole(part.connections)}
                    </td>
                  )}
                  <td data-label="Transaction ID age" className="pg-xid-cell">
                    <Wraparound name={part.name} ratio={part.wraparoundRatio} age={part.transactionIdAge} />
                  </td>
                  <td data-label="Dead rows" className="number">
                    {formatShare(part.deadRowsRatio)}
                    {part.deadRows !== null && <small> {formatRows(part.deadRows)}</small>}
                  </td>
                  {advanced && (
                    <td data-label="Manual vacuums / day" className="number">
                      {whole(part.vacuums)}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <SystemHidden hidden={database.hiddenDatabases} noun="database" />
      </section>
      <Tables tables={database.tables} hidden={database.hiddenTables} advanced={advanced} />
    </div>
  );
}

/** Props for {@link Tables}. */
interface TablesProps {
  /** The tables with the most dead rows, the most first; the catalog's only when System is on. */
  tables: DatabaseTable[];
  /** The catalog's tables the server left out. */
  hidden: HiddenRows;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** The tables with the most dead rows, with the share of their rows that are dead and their vacuums of the day. */
function Tables({ tables, hidden, advanced }: TablesProps) {
  const listed = tables;

  return (
    <section className="metric-panel" aria-labelledby="pg-tables">
      <PanelHead
        id="pg-tables"
        name="Dead rows"
        help={
          "The tables with the most dead rows: old versions of rows left by updates and " +
          "deletes, which take space and slow scans until a vacuum clears them. Read every " +
          "30 seconds. Amber from a fifth dead and red from half: autovacuum falls behind " +
          "on that table."
        }
      >
        <span>the tables with the most, until a vacuum clears them</span>
      </PanelHead>
      {listed.length === 0 ? (
        <p className="metrics-empty">No table has dead rows.</p>
      ) : (
        <table className="route-table pg-table">
          <thead>
            <tr>
              <th scope="col">Table</th>
              {advanced && (
                <th scope="col" className="number">
                  Live rows
                </th>
              )}
              <th scope="col" className="number">
                Dead rows
              </th>
              <th scope="col">Dead share</th>
              <th scope="col" className="number">
                Manual vacuums / day
              </th>
            </tr>
          </thead>
          <tbody>
            {listed.map((table) => (
              <tr key={`${table.database}.${table.schema ?? ""}.${table.name}`}>
                <th scope="row">
                  <code>{table.schema ? `${table.schema}.${table.name}` : table.name}</code>
                  <small className="pg-table-database"> in {table.database}</small>
                </th>
                {advanced && (
                  <td data-label="Live rows" className="number">
                    {formatRows(table.liveRows)}
                  </td>
                )}
                <td data-label="Dead rows" className="number">
                  {formatRows(table.deadRows)}
                </td>
                <td data-label="Dead share" className="pg-xid-cell">
                  <Share ratio={table.deadRowsRatio} label={`Dead rows of ${table.name}`} />
                </td>
                <td data-label="Manual vacuums / day" className="number">
                  {whole(table.vacuums)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <SystemHidden hidden={hidden} noun="table" />
    </section>
  );
}

/** Props for {@link Wraparound}. */
interface WraparoundProps {
  /** The database. */
  name: string;
  /** Its transaction ID age of the limit, from 0 to 1, or null when not reported. */
  ratio: number | null;
  /** The age, in transactions. */
  age: number | null;
}

/** How far a database's oldest transaction ID is to wraparound: a bar coloured at the wraparound rule's levels. */
function Wraparound({ name, ratio, age }: WraparoundProps) {
  if (ratio === null) {
    return <span className="pg-muted">not reported</span>;
  }

  const percent = Math.round(Math.min(Math.max(ratio, 0), 1) * 100);

  return (
    <span className="pg-meter" title={age === null ? undefined : `${formatRows(age)} of 2.1B transactions`}>
      <span
        className="disk-bar"
        role="meter"
        aria-label={`Transaction ID age of ${name}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
      >
        <i className={wraparoundTone(ratio)} style={{ width: `${percent}%` }} />
      </span>
      <b>{percent}%</b>
    </span>
  );
}

/** Props for {@link Share}. */
interface ShareProps {
  /** The share, from 0 to 1, or null when unknown. */
  ratio: number | null;
  /** What the bar measures, for assistive technology. */
  label: string;
}

/** A share of rows as a short bar: amber from a fifth, red from half, where vacuums fall behind. */
function Share({ ratio, label }: ShareProps) {
  if (ratio === null) {
    return <span className="pg-muted">–</span>;
  }

  const percent = Math.min(Math.max(ratio, 0), 1) * 100;
  const tone = ratio >= 0.5 ? "bad" : ratio >= 0.2 ? "warn" : "";

  return (
    <span className="pg-meter">
      <span
        className="disk-bar"
        role="meter"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(percent)}
      >
        <i className={tone} style={{ width: `${percent}%` }} />
      </span>
      <b>{formatShare(ratio)}</b>
    </span>
  );
}

/** Props for {@link Figure}. */
interface FigureProps {
  /** What the number is. */
  label: string;
  /** What the number means, behind the question mark beside its name (VEY-60). */
  help: string;
  /** The number now, formatted. */
  value: string;
  /** The unit after it. */
  unit: string;
  /** The number over the range. */
  line: ChartLine;
  /** Whether the number needs a look. */
  warn?: boolean;
}

/** One number with its line under it and a question mark that explains it, as on the SQL Server screen. */
function Figure({ label, help, value, unit, line, warn = false }: FigureProps) {
  return (
    <div className={warn ? "database-tile warn" : "database-tile"}>
      <TileName name={label} help={help} />
      <b>
        {value}
        <small> {unit}</small>
      </b>
      <Sparkline line={line} color={warn ? "var(--warning-fg)" : "var(--accent)"} />
    </div>
  );
}

/** A whole number with thousands separators, or a dash. */
function whole(value: number | null): string {
  return value === null ? "–" : Math.round(value).toLocaleString("en-US");
}

/** The sign of a growth: `+` when it grew, `−` when it shrank. */
function sign(value: number): string {
  return value < 0 ? "−" : "+";
}

/** Seconds as a clock, `6:52` for nearly seven minutes, `1:02:14` from an hour; under a minute, the seconds alone. */
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
