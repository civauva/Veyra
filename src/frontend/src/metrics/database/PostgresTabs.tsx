import type { ChartLine, DatabaseMetrics, DatabasePart, DatabaseTable } from "../../api/types";
import { LineChart } from "../LineChart";
import { formatAmount, formatRate, formatShare } from "../metricFormat";
import { Ring } from "../Ring";
import { Sparkline } from "../Sparkline";
import { connectionsRatio, connectionsTone, formatRows, wraparoundTone } from "./postgres";
import "./postgres.css";

/** Props for {@link PostgresOverview} and {@link PostgresDatabases}. */
export interface PostgresTabProps {
  /** The server's screen, from `GET /api/v1/metrics/databases/postgresql/{server}`. */
  database: DatabaseMetrics;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/**
 * A PostgreSQL server's Overview tab (VEY-33): its connections of the maximum and the database closest to transaction
 * ID wraparound as rings, then blocked sessions, deadlocks, the longest running query, and transactions now. The
 * Advanced view adds connections against the maximum and transactions over the range.
 */
export function PostgresOverview({ database, advanced }: PostgresTabProps) {
  const { card } = database;
  const ratio = connectionsRatio(card.connections, card.maxConnections);
  const oldest = database.databases
    .filter((part) => part.wraparoundRatio !== null)
    .reduce<DatabasePart | null>(
      (worst, part) => (worst === null || (part.wraparoundRatio ?? 0) > (worst.wraparoundRatio ?? 0) ? part : worst),
      null,
    );

  return (
    <>
      <section className="metric-panel pg-now" aria-labelledby="pg-now">
        <div className="metric-panel-head">
          <h2 id="pg-now">Now</h2>
          <span>{card.databases === 1 ? "1 database" : `${card.databases} databases`}</span>
        </div>
        <div className="pg-now-body">
          <div className="pg-rings">
            <Ring
              share={ratio}
              label="Connections"
              detail={`${whole(card.connections)} of ${whole(card.maxConnections)}`}
              tone={connectionsTone(ratio)}
            />
            <Ring
              share={card.wraparoundRatio}
              label="Wraparound"
              detail={oldest ? `oldest in ${oldest.name}` : "not reported"}
              tone={wraparoundTone(card.wraparoundRatio)}
            />
          </div>
          <div className="database-tiles pg-tiles">
            <Figure
              label="Blocked"
              value={whole(card.blocked)}
              unit="sessions"
              line={database.blocked}
              warn={card.blocked !== null && card.blocked > 0}
            />
            <Figure
              label="Deadlocks"
              value={whole(card.deadlocks)}
              unit="in 15 min"
              line={database.deadlocks}
              warn={card.deadlocks !== null && card.deadlocks > 0}
            />
            <Figure
              label="Longest query"
              value={card.longestQuerySeconds === null ? "–" : formatClock(card.longestQuerySeconds)}
              unit={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 60 ? "min" : "s"}
              line={database.longestQuery}
              warn={card.longestQuerySeconds !== null && card.longestQuerySeconds >= 300}
            />
            <Figure
              label="Commits"
              value={formatRate(card.commitsPerSecond)}
              unit="/s"
              line={database.commits ?? database.requests}
            />
            <Figure
              label="Rollbacks"
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
            <div className="metric-panel-head">
              <h2 id="pg-connections">Connections</h2>
              <span>in use against the maximum</span>
            </div>
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
            <div className="metric-panel-head">
              <h2 id="pg-transactions">Transactions</h2>
              <span>committed and rolled back per second</span>
            </div>
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
  return (
    <div className="metric-panels pg-panels">
      <section className="metric-panel" aria-labelledby="pg-databases">
        <div className="metric-panel-head">
          <h2 id="pg-databases">Databases</h2>
          <span>{database.databases.length === 1 ? "1 database" : `${database.databases.length} databases`}</span>
        </div>
        {database.databases.length === 0 ? (
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
              {database.databases.map((part) => (
                <tr key={part.name}>
                  <th scope="row">{part.name}</th>
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
      </section>
      <Tables tables={database.tables} advanced={advanced} />
    </div>
  );
}

/** Props for {@link Tables}. */
interface TablesProps {
  /** The tables with the most dead rows, the most first. */
  tables: DatabaseTable[];
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** The tables with the most dead rows, with the share of their rows that are dead and their vacuums of the day. */
function Tables({ tables, advanced }: TablesProps) {
  return (
    <section className="metric-panel" aria-labelledby="pg-tables">
      <div className="metric-panel-head">
        <h2 id="pg-tables">Dead rows</h2>
        <span>the tables with the most, until a vacuum clears them</span>
      </div>
      {tables.length === 0 ? (
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
            {tables.map((table) => (
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
  /** The number now, formatted. */
  value: string;
  /** The unit after it. */
  unit: string;
  /** The number over the range. */
  line: ChartLine;
  /** Whether the number needs a look. */
  warn?: boolean;
}

/** One number with its line under it, as on the SQL Server screen. */
function Figure({ label, value, unit, line, warn = false }: FigureProps) {
  return (
    <div className={warn ? "database-tile warn" : "database-tile"}>
      <span>{label}</span>
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
