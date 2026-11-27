import { Link } from "react-router";

import { alertTitle } from "../../../alerts/ruleWords";
import type { AlertListItem, BackupType, ServerArea, ServerBackups, ServerOverview } from "../../../api/types";
import { formatBytes } from "../../../home/homeFormat";
import { LineChart } from "../../../metrics/LineChart";
import { formatAmount } from "../../../metrics/metricFormat";
import { MiniLine } from "../../../resources/MiniLine";
import { HelpTip } from "../../../ui/HelpTip";
import { Change, Donut, growthWords, lineOver, partColors, SqlFigure, timeWords, waitClock } from "./sqlParts";

/** Props for {@link SqlOverviewTab}. */
export interface SqlOverviewTabProps {
  /** The overview read. */
  overview: ServerOverview;
  /** The backups of the range, or null while they are read. */
  backups: ServerBackups | null;
  /** The server's open alerts, or null while they are read. */
  alerts: AlertListItem[] | null;
  /** Opens another tab. */
  onTab(tab: "blocking" | "backups" | "storage" | "server" | "live" | "queries"): void;
}

/** The names of the core counters, as reference screenshot 1 words them. */
const counterNames: Record<ServerOverview["counters"][number]["name"], string> = {
  batchRequests: "Batch requests / s",
  compilations: "Compilations / s",
  pageLife: "Page life expectancy",
  waits: "Wait time / s",
};

/** Whether a rise of a counter is bad: more compilations and waits are, more batches and page life are not. */
const counterUpIsBad: Record<ServerOverview["counters"][number]["name"], boolean> = {
  batchRequests: false,
  compilations: true,
  pageLife: false,
  waits: true,
};

/** The areas of coverage in words. */
const areaNames: Record<ServerArea, string> = {
  activity: "Activity",
  waits: "Waits",
  queries: "Queries",
  storage: "Storage",
  backups: "Backups",
  jobs: "Jobs",
  events: "Events",
  configuration: "Configuration",
  availability: "Availability",
};

/**
 * A SQL Server at a glance (VEY-75, reference screenshots 1 and 8): the figures with their change, the instance's
 * performance, the core counters and I/O, the top waits, and a panel each for backups, blocking, and storage, then the
 * databases and what is monitored. Every number comes from the server (ADR 0025).
 */
export function SqlOverviewTab({ overview, backups, alerts, onTab }: SqlOverviewTabProps) {
  const { figures, machine } = overview;
  const memoryShare =
    figures.sqlMemory.value !== null && machine.memoryBytes ? figures.sqlMemory.value / machine.memoryBytes : null;
  const free = figures.volumeFree.value;

  return (
    <>
      <section className="sq-figures" aria-label="Figures">
        <SqlFigure
          label="Instance CPU"
          onOpen={() => onTab("live")}
          value={figures.cpu.value === null ? "—" : `${Math.round(figures.cpu.value * 100)}%`}
          detail={<Change change={figures.cpu.change} upIsBad points short />}
          line={figures.cpu.line}
          help="The share of the machine's CPU that SQL Server used, now and over the range, against the period before."
        />
        <SqlFigure
          label="SQL memory"
          value={
            <>
              {formatAmount(figures.sqlMemory.value)}
              {machine.memoryBytes ? <small> / {formatBytes(machine.memoryBytes)}</small> : null}
            </>
          }
          detail={memoryShare === null ? undefined : `${(memoryShare * 100).toFixed(1)}% of the machine`}
          line={figures.sqlMemory.line}
          help="The memory SQL Server holds, against the machine's physical memory. SQL Server keeps what it takes, so a high share is normal; a falling page life says it is short."
        />
        <SqlFigure
          label="Active sessions"
          onOpen={() => onTab("live")}
          value={figures.sessions.value === null ? "—" : Math.round(figures.sessions.value).toLocaleString("en")}
          detail={<Change change={figures.sessions.change} upIsBad={false} short />}
          line={figures.sessions.line}
          help="User connections to the instance, against the period before."
        />
        <SqlFigure
          label="Blocked sessions"
          onOpen={() => onTab("blocking")}
          value={figures.blocked.value === null ? "—" : Math.round(figures.blocked.value)}
          detail={`${overview.blocking.chains.length} ${overview.blocking.chains.length === 1 ? "chain" : "chains"} now`}
          line={figures.blocked.line}
          tone={(figures.blocked.value ?? 0) > 0 ? "warn" : undefined}
          help="Sessions waiting for a lock another session holds. Blocking opens the chains and the statement at their head."
        />
        <SqlFigure
          label="Database size"
          onOpen={() => onTab("storage")}
          value={formatAmount(figures.size.value)}
          detail={`${growthWords(overview.storage.growth24hBytes, formatBytes)} in 24 h`}
          line={figures.size.line}
          help="Data and log files of every database together, and how much they grew in the last 24 hours."
        />
        <SqlFigure
          label="Last full backup"
          onOpen={() => onTab("backups")}
          value={timeWords(figures.lastFullBackupSeconds.value)}
          detail="the oldest of the databases"
          help="How long ago the database with the oldest full backup had its last one. Backups shows each database against the policy."
        />
        <SqlFigure
          label="Batch requests"
          onOpen={() => onTab("queries")}
          value={
            <>
              {figures.batchRequests.value === null
                ? "—"
                : Math.round(figures.batchRequests.value).toLocaleString("en")}
              <small>/s</small>
            </>
          }
          detail={<Change change={figures.batchRequests.change} upIsBad={false} short />}
          line={figures.batchRequests.line}
          help="Statement batches the instance ran per second: how busy it is, against the period before."
        />
        <SqlFigure
          label={`Volume ${figures.volumeFree.volume ?? ""} free`.replace("  ", " ")}
          onOpen={() => onTab("storage")}
          value={free === null ? "—" : `${Math.round(free * 100)}%`}
          detail={fullIn(overview, figures.volumeFree.volume)}
          line={figures.volumeFree.line}
          tone={free === null ? undefined : free < 0.15 ? "bad" : free < 0.25 ? "warn" : undefined}
          help="The free share of the fullest volume that holds database files, and when it fills at the pace of the last days."
        />
      </section>

      <div className="sq-row sq-row-main">
        <section className="cn-card" aria-labelledby="sq-performance">
          <header>
            <h2 id="sq-performance">Instance performance</h2>
            <HelpTip label="Instance performance">
              SQL Server&apos;s share of the CPU and of the machine&apos;s memory over the range, and the time sessions
              spent waiting each second, idle waits left out.
            </HelpTip>
          </header>
          <div className="sq-chart">
            <LineChart
              title="CPU and memory"
              format={(value) => `${Math.round(value * 100)}%`}
              max={1}
              height={200}
              series={[
                {
                  line: lineOver(
                    "CPU (instance)",
                    overview.performance.cpu,
                    overview.from,
                    overview.to,
                    overview.performance.starts,
                  ),
                  color: "var(--accent)",
                  filled: true,
                },
                {
                  line: lineOver(
                    "Memory (SQL)",
                    overview.performance.sqlMemoryRatio,
                    overview.from,
                    overview.to,
                    overview.performance.starts,
                  ),
                  color: "var(--info-fg)",
                },
              ]}
            />
            <div className="sq-waitline">
              <span>Wait time per second</span>
              <MiniLine values={overview.performance.waitMsPerSecond} tone="bad" className="sq-waitline-line" />
              <b>{lastOf(overview.performance.waitMsPerSecond)}</b>
            </div>
          </div>
        </section>
        <section className="cn-card" aria-labelledby="sq-counters">
          <header>
            <h2 id="sq-counters">Core counters</h2>
            <HelpTip label="Core counters">
              Averages over the range against the period before. Compilations rise when plans are not reused; page life
              falls when memory is short; waits say how long sessions stood still.
            </HelpTip>
            <span className="cn-meta">vs the period before</span>
          </header>
          <dl className="sq-counters">
            {overview.counters.map((counter) => (
              <div key={counter.name}>
                <dt>{counterNames[counter.name]}</dt>
                <dd>
                  <b>{counterValue(counter)}</b>
                  <Change change={counter.change} upIsBad={counterUpIsBad[counter.name]} />
                </dd>
              </div>
            ))}
          </dl>
          <div className="sq-io">
            <div>
              <small>I/O reads</small>
              <b>{formatAmount(overview.io.readBytesPerSecond, true)}</b>
              <MiniLine values={overview.io.readLine} tone="ok" className="sq-io-line" />
            </div>
            <div>
              <small>I/O writes</small>
              <b>{formatAmount(overview.io.writeBytesPerSecond, true)}</b>
              <MiniLine values={overview.io.writeLine} tone="ok" className="sq-io-line" />
            </div>
          </div>
        </section>
      </div>

      <div className="sq-row sq-row-four">
        <Waits overview={overview} onMore={() => onTab("blocking")} />
        <BackupsPanel backups={backups} onMore={() => onTab("backups")} />
        <BlockingPanel overview={overview} onMore={() => onTab("blocking")} />
        <StoragePanel overview={overview} onMore={() => onTab("storage")} />
      </div>

      <div className="sq-row sq-row-main">
        <Inventory overview={overview} />
        <CoverageAndAlerts overview={overview} alerts={alerts} />
      </div>
    </>
  );
}

/** A counter's value in its unit. */
function counterValue(counter: ServerOverview["counters"][number]): string {
  if (counter.value === null) {
    return "—";
  }

  if (counter.name === "pageLife") {
    return `${Math.round(counter.value).toLocaleString("en")} s`;
  }

  if (counter.name === "waits") {
    return `${Math.round(counter.value).toLocaleString("en")} ms`;
  }

  return Math.round(counter.value).toLocaleString("en");
}

/** The latest value of a line in milliseconds. */
function lastOf(values: (number | null)[]): string {
  const last = values.filter((value): value is number => value !== null).at(-1);

  return last === undefined ? "—" : `${Math.round(last)} ms`;
}

/** When a volume fills, in words. */
function fullIn(overview: ServerOverview, name: string | null): string {
  const volume = overview.storage.volumes.find((one) => one.name === name);

  if (!volume || volume.daysUntilFull === null) {
    return "not filling";
  }

  return `full in ~${timeWords(volume.daysUntilFull * 86_400)}`;
}

/** The top wait types as a donut and a list. */
export function Waits({ overview, onMore }: { overview: ServerOverview; onMore?: () => void }) {
  const types = overview.waits.types;

  return (
    <section className="cn-card" aria-labelledby="sq-waits">
      <header>
        <h2 id="sq-waits">Top wait types</h2>
        <HelpTip label="Top wait types">
          What sessions waited for over the range, by share of the time, idle waits left out. Locks mean blocking,
          PAGEIOLATCH means reading from disk, CXPACKET means parallel plans.
        </HelpTip>
      </header>
      {types.length === 0 ? (
        <p className="cn-empty">Nothing waited in the range.</p>
      ) : (
        <div className="sq-waits">
          <Donut
            center={timeWords(overview.waits.totalSeconds)}
            under="waited"
            parts={types.map((one, index) => ({
              name: one.type,
              share: one.share,
              color: partColors[index] ?? "var(--faint)",
            }))}
          />
          <ul>
            {types.map((one, index) => (
              <li key={one.type}>
                <i style={{ background: partColors[index] ?? "var(--faint)" }} aria-hidden="true" />
                <span className="mono">{one.type}</span>
                <span className="sq-muted">{timeWords(one.seconds)}</span>
                <b>{Math.round(one.share * 100)}%</b>
              </li>
            ))}
          </ul>
        </div>
      )}
      {onMore && (
        <footer>
          <span>Idle waits are left out on the server</span>
          <button type="button" className="link-button" onClick={onMore}>
            Waits and blocking →
          </button>
        </footer>
      )}
    </section>
  );
}

/** The newest backup of each type, the overdue databases, and full backup time per day. */
function BackupsPanel({ backups, onMore }: { backups: ServerBackups | null; onMore(): void }) {
  const types: [BackupType, string][] = [
    ["full", "Full"],
    ["differential", "Differential"],
    ["log", "Log"],
  ];
  const overdue = backups?.freshness.filter((one) => one.state === "warning" || one.state === "caution").length ?? 0;

  return (
    <section className="cn-card" aria-labelledby="sq-backups">
      <header>
        <h2 id="sq-backups">Backups</h2>
        <HelpTip label="Backups">
          The newest backup of each type in the range, and how long full backups took each day. A database is overdue
          when its backups are older than the backup alert rules allow.
        </HelpTip>
      </header>
      {!backups ? (
        <p className="cn-empty">Reading the backup history…</p>
      ) : backups.selfManaged ? (
        <p className="cn-empty">
          This platform backs up its databases by itself, so there is no backup history to read.
        </p>
      ) : (
        <>
          <div className={overdue > 0 ? "sq-banner warn" : "sq-banner ok"}>
            <b>
              {overdue > 0
                ? `${overdue} ${overdue === 1 ? "database" : "databases"} overdue`
                : "Every database is backed up"}
            </b>
            <small>{backups.backups.length.toLocaleString("en")} backups in the range</small>
          </div>
          <table className="cn-table">
            <thead>
              <tr>
                <th scope="col">Type</th>
                <th scope="col">Newest</th>
                <th scope="col" className="num">
                  Age
                </th>
              </tr>
            </thead>
            <tbody>
              {types.map(([type, name]) => {
                const newest = backups.backups.find((one) => one.type === type);

                return (
                  <tr key={type}>
                    <td>{name}</td>
                    <td>{newest ? newest.database : "none in the range"}</td>
                    <td className="num">
                      {newest ? timeWords((Date.parse(backups.to) - Date.parse(newest.finishedAt)) / 1000) : "—"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <div className="sq-bars" aria-label="Full backup minutes per day">
            {backups.days.slice(-7).map((day) => {
              const most = Math.max(1, ...backups.days.slice(-7).map((one) => one.full.seconds));

              return (
                <span key={day.day} title={`${day.day}: ${timeWords(day.full.seconds)}`}>
                  <i
                    style={{ height: `${Math.max(day.full.seconds > 0 ? 4 : 0, (day.full.seconds / most) * 100)}%` }}
                  />
                </span>
              );
            })}
          </div>
        </>
      )}
      <footer>
        <span>Full backup time per day</span>
        <button type="button" className="link-button" onClick={onMore}>
          Backups →
        </button>
      </footer>
    </section>
  );
}

/** The blocking chains now and blocked sessions over the range. */
function BlockingPanel({ overview, onMore }: { overview: ServerOverview; onMore(): void }) {
  const chains = overview.blocking.chains;
  const blocked = chains.reduce((sum, chain) => sum + chain.blockedCount, 0);

  return (
    <section className="cn-card" aria-labelledby="sq-blocking">
      <header>
        <h2 id="sq-blocking">Blocking</h2>
        <HelpTip label="Blocking">
          Sessions at the head of a blocking chain in the latest live read, and how many sessions were blocked over the
          range.
        </HelpTip>
      </header>
      <div className={chains.length > 0 ? "sq-banner bad" : "sq-banner ok"}>
        <b>
          {chains.length > 0
            ? `${chains.length} blocking ${chains.length === 1 ? "session" : "sessions"}`
            : "Nothing is blocked"}
        </b>
        <small>{chains.length > 0 ? `${blocked} sessions are blocked now` : "in the latest live read"}</small>
      </div>
      {chains.length > 0 && (
        <div className="cn-table-scroll">
          <table className="cn-table sq-tight">
            <thead>
              <tr>
                <th scope="col">Session</th>
                <th scope="col">Wait type</th>
                <th scope="col" className="num">
                  Blocks
                </th>
                <th scope="col" className="num">
                  For
                </th>
              </tr>
            </thead>
            <tbody>
              {chains.slice(0, 4).map((chain) => (
                <tr key={chain.headSessionId}>
                  <td>{chain.headSessionId}</td>
                  <td className="mono">{chain.waitType ?? "—"}</td>
                  <td className="num">{chain.blockedCount}</td>
                  <td className="num mono">{waitClock(chain.waitSeconds)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className="sq-subline">
        <span>Blocked sessions over the range</span>
        <MiniLine values={overview.blocking.blockedLine} tone="bad" className="sq-blocked-line" />
      </div>
      <footer>
        <span>{overview.blocking.deadlocks} deadlocks in the range</span>
        <button type="button" className="link-button" onClick={onMore}>
          Blocking →
        </button>
      </footer>
    </section>
  );
}

/** Total size, the fullest volume, and the largest databases with their growth. */
function StoragePanel({ overview, onMore }: { overview: ServerOverview; onMore(): void }) {
  const storage = overview.storage;
  const fullest = [...storage.volumes].sort((a, b) => a.freeRatio - b.freeRatio)[0];

  return (
    <section className="cn-card" aria-labelledby="sq-storage">
      <header>
        <h2 id="sq-storage">Storage</h2>
        <HelpTip label="Storage">
          All databases together and their growth in 24 hours, the fullest volume, and the largest databases with what
          they grew.
        </HelpTip>
      </header>
      <div className="sq-minis">
        <div>
          <small>Database size</small>
          <b>{formatAmount(storage.totalBytes)}</b>
          <span className="sq-muted">{growthWords(storage.growth24hBytes, formatBytes)} in 24 h</span>
        </div>
        {fullest && (
          <div className={fullest.freeRatio < 0.15 ? "bad" : undefined}>
            <small>Volume {fullest.name} free</small>
            <b>{Math.round(fullest.freeRatio * 100)}%</b>
            <span className="sq-muted">{formatBytes(fullest.freeBytes)} free</span>
          </div>
        )}
      </div>
      <h3 className="cn-section sq-pad">Largest databases</h3>
      <dl className="sq-largest">
        {storage.largest.slice(0, 5).map((one) => (
          <div key={one.database}>
            <dt>{one.database}</dt>
            <dd>
              {formatBytes(one.bytes)}
              <span className={(one.growth24hBytes ?? 0) > 0 ? "sq-grow" : "sq-muted"}>
                {growthWords(one.growth24hBytes, formatBytes)}
              </span>
            </dd>
          </div>
        ))}
      </dl>
      <footer>
        <span>Growth over 24 h</span>
        <button type="button" className="link-button" onClick={onMore}>
          Storage →
        </button>
      </footer>
    </section>
  );
}

/** Each database with its files, growth, recovery model, and state. */
function Inventory({ overview }: { overview: ServerOverview }) {
  return (
    <section className="cn-card" aria-labelledby="sq-inventory">
      <header>
        <h2 id="sq-inventory">Database inventory</h2>
        <HelpTip label="Database inventory">
          Every database with its data and log files, how much it grows a day, its recovery model, and its state. The
          server leaves its own databases out unless System is on.
        </HelpTip>
        <span className="cn-meta">{overview.inventory.length} databases</span>
      </header>
      <div className="cn-table-scroll">
        <table className="cn-table">
          <thead>
            <tr>
              <th scope="col">Database</th>
              <th scope="col" className="num">
                Data
              </th>
              <th scope="col" className="num">
                Log
              </th>
              <th scope="col" className="num">
                Growth / day
              </th>
              <th scope="col" className="wide">
                Recovery
              </th>
              <th scope="col">State</th>
            </tr>
          </thead>
          <tbody>
            {overview.inventory.map((one) => (
              <tr key={one.database}>
                <td>{one.database}</td>
                <td className="num">{formatAmount(one.dataBytes)}</td>
                <td className="num">{formatAmount(one.logBytes)}</td>
                <td className="num">{growthWords(one.growthPerDayBytes, formatBytes)}</td>
                <td className="wide">{one.recoveryModel ?? "—"}</td>
                <td>
                  <span
                    className={
                      one.state === null || one.state.toLowerCase() === "online" ? "cn-pill ok" : "cn-pill warn"
                    }
                  >
                    {one.state ?? "Unknown"}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** The server's open alerts, and which monitoring areas deliver data. */
function CoverageAndAlerts({ overview, alerts }: { overview: ServerOverview; alerts: AlertListItem[] | null }) {
  return (
    <section className="cn-card" aria-labelledby="sq-coverage">
      <header>
        <h2 id="sq-coverage">Alerts and coverage</h2>
        <HelpTip label="Alerts and coverage">
          The server&apos;s open alerts, and each monitoring area with whether its data arrives. An area that is silent
          names the permission its login lacks, when that is the reason.
        </HelpTip>
        <span className="cn-meta">{alerts === null ? "…" : `${alerts.length} open`}</span>
      </header>
      <div className="sq-pad">
        {alerts !== null && alerts.length === 0 && <p className="sq-muted">No alert is open.</p>}
        <ul className="sq-alerts">
          {(alerts ?? []).slice(0, 4).map((alert) => (
            <li key={alert.id}>
              <Link to={`/alerts/${encodeURIComponent(alert.id)}`}>
                {alertTitle(alert.rule, alert.block, alert.value, false)}
              </Link>
              <span className={alert.severity === "Warning" ? "cn-pill bad" : "cn-pill warn"}>
                {alert.severity === "Warning" ? "Warning" : "Caution"}
              </span>
            </li>
          ))}
        </ul>
        <h3 className="cn-section">Monitoring coverage</h3>
        <ul className="sq-coverage">
          {overview.coverage.map((one) => (
            <li
              key={one.area}
              className={one.delivering ? "on" : "off"}
              title={one.missingPermission ? `Needs ${one.missingPermission}` : undefined}
            >
              <span aria-hidden="true">{one.delivering ? "●" : "○"}</span> {areaNames[one.area]}
              <span className="visually-hidden">{one.delivering ? " delivers data" : " sends nothing"}</span>
            </li>
          ))}
        </ul>
        {overview.coverage
          .filter((one) => !one.delivering && one.missingPermission)
          .map((one) => (
            <p key={one.area} className="sq-muted sq-small">
              {areaNames[one.area]}: the login needs {one.missingPermission}.
            </p>
          ))}
      </div>
    </section>
  );
}
