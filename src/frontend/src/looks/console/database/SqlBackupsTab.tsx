import { Link } from "react-router";

import { rulePath } from "../../../alerts/rulePath";
import { showPart } from "../../../metrics/database/drill";
import type { BackupType, ServerBackups } from "../../../api/types";
import { formatBytes } from "../../../home/homeFormat";
import { formatClock } from "../../../logs/format";
import { StackedBars } from "../../../overview/StackedBars";
import { localDay, shortDate } from "../../../time/zone";
import { HelpTip } from "../../../ui/HelpTip";
import { SqlFigure, timeWords } from "./sqlParts";

/** Props for {@link SqlBackupsTab}. */
export interface SqlBackupsTabProps {
  /** The backups of the range, or null while they are read. */
  backups: ServerBackups | null;
  /** Why they could not be read, or null. */
  error: string | null;
  /** The server as its alerts name it, its engine and server, for what a late backup means and how to fix it. */
  block: string;
}

/** The types in words. */
const typeNames: Record<BackupType, string> = { full: "Full", differential: "Differential", log: "Log" };

/** The freshness states in words, with their pill. */
const stateWords = {
  ok: ["OK", "ok"],
  caution: ["Due", "warn"],
  warning: ["Overdue", "bad"],
  ignored: ["Ignored", "off"],
} as const;

/**
 * A SQL Server's backups (VEY-75, reference screenshot 8): freshness per database against the backup rules, time per
 * day by type, a grid of the last 7 days, and each backup of the range (ADR 0025).
 */
export function SqlBackupsTab({ backups, error, block }: SqlBackupsTabProps) {
  if (error && !backups) {
    return (
      <p className="cn-error" role="alert">
        Could not read the backups: {error}
      </p>
    );
  }

  if (!backups) {
    return <p className="cn-empty">Reading the backup history…</p>;
  }

  if (backups.selfManaged) {
    return (
      <p className="cn-empty">
        This platform backs up its databases by itself and keeps no backup history Veyra can read. Its portal shows the
        restore points.
      </p>
    );
  }

  const late = backups.freshness.filter((one) => one.state === "warning" || one.state === "caution");
  const counted = backups.freshness.filter((one) => one.state !== "ignored");
  const counts = (type: BackupType) => backups.backups.filter((one) => one.type === type).length;
  const fulls = backups.backups.filter((one) => one.type === "full");
  const fullSeconds = fulls.length > 0 ? fulls.reduce((sum, one) => sum + one.seconds, 0) / fulls.length : null;
  const policy = backups.freshness[0]?.policy;

  return (
    <>
      <section className="sq-figures sq-figures-five" aria-label="Figures">
        <SqlFigure
          label="Databases backed up"
          value={
            <>
              {counted.length - late.length}
              <small> / {counted.length}</small>
            </>
          }
          detail={late.length > 0 ? `${late.map((one) => one.database).join(", ")} late` : "every one on time"}
          tone={late.some((one) => one.state === "warning") ? "bad" : late.length > 0 ? "warn" : undefined}
          help="Databases whose backups are as recent as the backup alert rules ask, of those the rules watch."
          onOpen={() => showPart("sq-freshness-card")}
        />
        <SqlFigure
          label="Backups in the range"
          value={backups.backups.length.toLocaleString("en")}
          detail={`${counts("full")} full · ${counts("differential")} diff · ${counts("log")} log`}
          help="Backups that finished in the top bar's range, by type, from the server's backup history."
          onOpen={() => showPart("sq-history-card")}
        />
        <SqlFigure
          label="Full backup time"
          value={timeWords(fullSeconds)}
          detail="on average in the range"
          help="How long a full backup took on average. A rise points at slower storage or a larger database."
          onOpen={() => showPart("sq-duration-card")}
        />
        <SqlFigure
          label="Backed up"
          value={formatBytes(backups.backups.reduce((sum, one) => sum + (one.compressedBytes ?? one.bytes ?? 0), 0))}
          detail="written in the range"
          help="What the backups of the range wrote, compressed where they were."
          onOpen={() => showPart("sq-history-card")}
        />
        <SqlFigure
          label="Copy-only backups"
          value={backups.backups.filter((one) => one.copyOnly).length}
          detail="outside the chain"
          help="Backups taken with COPY_ONLY, which leave the regular chain of differential and log backups alone."
          onOpen={() => showPart("sq-history-card")}
        />
      </section>

      <div className="sq-row sq-row-main">
        <section id="sq-freshness-card" className="cn-card" aria-labelledby="sq-freshness" tabIndex={-1}>
          <header>
            <h2 id="sq-freshness">Backup freshness</h2>
            <HelpTip label="Backup freshness">
              Each database&apos;s newest full, differential, and log backup against the policy, worst first. The policy
              uses the backup alert rules&apos; thresholds. A database that never had a full backup counts as overdue
              here.
            </HelpTip>
            {policy && <span className="cn-meta">{policy}</span>}
          </header>
          <div className="cn-table-scroll">
            <table className="cn-table">
              <thead>
                <tr>
                  <th scope="col">Database</th>
                  <th scope="col" className="num">
                    Full
                  </th>
                  <th scope="col" className="num">
                    Diff
                  </th>
                  <th scope="col" className="num">
                    Log
                  </th>
                  <th scope="col" className="wide">
                    Recovery
                  </th>
                  <th scope="col">Policy</th>
                </tr>
              </thead>
              <tbody>
                {backups.freshness.map((one) => (
                  <tr key={one.database}>
                    <td>{one.database}</td>
                    <td className={one.state === "warning" ? "num cn-bad" : "num"}>{timeWords(one.fullAgeSeconds)}</td>
                    <td className="num">{timeWords(one.diffAgeSeconds)}</td>
                    <td className="num">{timeWords(one.logAgeSeconds)}</td>
                    <td className="wide">{one.recoveryModel ?? "—"}</td>
                    <td>
                      {one.state === "warning" || one.state === "caution" ? (
                        // A late backup leads to what it means and how to fix it (VEY-83).
                        <Link
                          className={`cn-pill ${stateWords[one.state][1]}`}
                          to={rulePath("db-backup-age", { kind: "database", name: block, part: one.database }) + "#fix"}
                          title={`How to fix the backups of ${one.database}`}
                        >
                          {stateWords[one.state][0]}
                        </Link>
                      ) : (
                        <span className={`cn-pill ${stateWords[one.state][1]}`}>{stateWords[one.state][0]}</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
        <section id="sq-duration-card" className="cn-card" aria-labelledby="sq-duration" tabIndex={-1}>
          <header>
            <h2 id="sq-duration">Backup time per day</h2>
            <HelpTip label="Backup time per day">
              How long the backups of each day took together, stacked by type.
            </HelpTip>
          </header>
          {backups.days.length > 0 && (
            <div className="sq-pad">
              <StackedBars
                title="Backup minutes per day"
                steps={backups.days.map((day) => shortDate(day.day))}
                format={(minutes) => `${Math.round(minutes)} min`}
                labels={[
                  shortDate(backups.days[0]?.day ?? backups.from),
                  shortDate(backups.days[Math.floor(backups.days.length / 2)]?.day ?? backups.from),
                  "today",
                ]}
                layers={[
                  { name: "Full", color: "var(--accent)", values: backups.days.map((day) => day.full.seconds / 60) },
                  {
                    name: "Differential",
                    color: "var(--info-fg)",
                    values: backups.days.map((day) => day.differential.seconds / 60),
                  },
                  {
                    name: "Log",
                    color: "var(--cn-violet, #a78bfa)",
                    values: backups.days.map((day) => day.log.seconds / 60),
                  },
                ]}
              />
            </div>
          )}
        </section>
      </div>

      <div className="sq-row sq-row-main">
        <section id="sq-grid-card" className="cn-card advanced-only" aria-labelledby="sq-grid" tabIndex={-1}>
          <header>
            <h2 id="sq-grid">The last 7 days</h2>
            <HelpTip label="The last 7 days">
              Which backups each database had on each of the last 7 days: F for full, D for differential, and the number
              of log backups. An empty day had none.
            </HelpTip>
          </header>
          <div className="cn-table-scroll">
            <table className="sq-grid">
              <thead>
                <tr>
                  <th scope="col">Database</th>
                  {backups.grid.days.map((day) => (
                    <th key={day} scope="col">
                      {shortDate(day)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {backups.grid.databases.map((row) => (
                  <tr key={row.database}>
                    <th scope="row">{row.database}</th>
                    {row.cells.map((cell, index) => (
                      <td key={index}>
                        {cell.full && <span className="sq-bk full">F</span>}
                        {cell.differential && <span className="sq-bk diff">D</span>}
                        {cell.log > 0 && <span className="sq-bk log">L·{cell.log}</span>}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
        <section id="sq-history-card" className="cn-card" aria-labelledby="sq-history" tabIndex={-1}>
          <header>
            <h2 id="sq-history">Backup history</h2>
            <HelpTip label="Backup history">
              Each backup of the range, newest first: when it finished, how long it took, what it wrote, and where.
            </HelpTip>
            <span className="cn-meta">{backups.backups.length} in the range</span>
          </header>
          <div className="cn-table-scroll sq-history">
            <table className="cn-table">
              <thead>
                <tr>
                  <th scope="col">Finished</th>
                  <th scope="col">Database</th>
                  <th scope="col">Type</th>
                  <th scope="col" className="num">
                    Took
                  </th>
                  <th scope="col" className="num">
                    Size
                  </th>
                </tr>
              </thead>
              <tbody>
                {backups.backups.slice(0, 100).map((one) => (
                  <tr key={`${one.database}-${one.finishedAt}-${one.type}`} title={one.device ?? undefined}>
                    <td>
                      {localDay(one.finishedAt) === localDay(backups.to) ? "" : `${shortDate(one.finishedAt)} `}
                      {formatClock(one.finishedAt)}
                    </td>
                    <td>{one.database}</td>
                    <td>
                      {typeNames[one.type]}
                      {one.copyOnly ? " (copy)" : ""}
                    </td>
                    <td className="num">{timeWords(one.seconds)}</td>
                    <td className="num">{one.bytes === null ? "—" : formatBytes(one.compressedBytes ?? one.bytes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </>
  );
}
