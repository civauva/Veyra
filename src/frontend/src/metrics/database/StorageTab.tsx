import { useState, type ReactNode } from "react";
import { unreadText } from "./unread";

import { api } from "../../api/client";
import type { DatabaseStorage, PlanWarning, StorageView } from "../../api/types";
import { formatAmount, formatSpan } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { databaseDetailId, databaseRowId, showPart } from "./drill";
import { PanelHead } from "./helpNames";
import { copyText } from "./liveFormat";
import { SystemHidden, useSystemShown } from "./SystemFilter";

/** Props for {@link StorageTab}. */
export interface StorageTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** Whether the login lacks a permission this tab needs, so an empty answer proves nothing (VEY-58). */
  blind?: boolean;
}

/**
 * The storage part of a SQL Server's Databases tab (ADR 0020, areas 6 to 8): what needs doing, in words with the
 * statement to run; the volumes and when they fill; and in the Advanced view each database's settings, files, log,
 * backups, and integrity.
 */
export function StorageTab({ engine, server, advanced, blind = false }: StorageTabProps) {
  const [systemShown] = useSystemShown();
  // The server leaves out its own databases, such as msdb, and their findings unless System is on (VEY-64, VEY-66).
  const { data, notFound, error } = useMetricData(
    `storage-${engine}-${server}`,
    (signal) => api.databaseStorage(engine, server, systemShown, signal),
    { choices: String(systemShown) },
  );

  if (notFound) {
    return (
      <p className="metrics-empty">
        The agent has not sent a storage read yet; it reads files, logs, backups, and integrity every 5 minutes.
      </p>
    );
  }

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        Storage could not be loaded: {error}
      </p>
    ) : null;
  }

  // The server merges the findings and ranks them, each with its database so it can lead to it (VEY-62, VEY-68).
  const findings = data.needsDoing;

  return (
    <>
      <section className="metric-panel" aria-labelledby="storage-findings">
        <PanelHead
          id="storage-findings"
          name="Needs doing"
          help={
            "What Veyra found in the databases' files, logs, backups, and integrity checks, " +
            "worst first, each with the statement to run. Read every 5 minutes from " +
            "sys.databases, sys.master_files, sys.dm_db_log_stats, and msdb's backup history. " +
            "Nothing here means they all look right."
          }
        >
          <span>files, logs, backups, and integrity</span>
        </PanelHead>
        {findings.length === 0 ? (
          <p className="metrics-empty">
            {blind
              ? unreadText
              : data.hiddenFindings.count > 0
                ? "Nothing on the applications' databases."
                : "Nothing: files, logs, backups, and integrity checks all look right."}
          </p>
        ) : (
          <ul className="live-findings">
            {findings.map(({ finding, database }, index) => (
              <FindingItem key={`${finding.kind}-${index}`} finding={finding}>
                {database !== null && <DatabaseLink name={database} advanced={advanced} />}
              </FindingItem>
            ))}
          </ul>
        )}
        <SystemHidden hidden={data.hiddenFindings} noun="finding" />
      </section>

      <Volumes data={data} />

      {advanced && (
        <section className="metric-panel" aria-labelledby="storage-detail">
          <PanelHead
            id="storage-detail"
            name="Each database"
            help={
              "Each database's settings, log, backups, integrity, and files, read every 5 " +
              "minutes; open one to see them. Look for auto close or auto shrink on, page " +
              "verify not CHECKSUM, a log that waits to be reused, a log backup over an hour " +
              "old in full recovery, a last good CHECKDB over 8 days ago, and growth set in " +
              "percent."
            }
          >
            <span>settings, files, log, backups, integrity</span>
          </PanelHead>
          {data.databases.map((database) => (
            <DatabaseDetail key={database.name} database={database} />
          ))}
          <SystemHidden hidden={data.hiddenDatabases} noun="database" />
        </section>
      )}
    </>
  );
}

/** The volumes that hold database files, and when each fills. */
function Volumes({ data }: { data: StorageView }) {
  if (data.volumes.length === 0) {
    return null;
  }

  return (
    <section className="metric-panel" aria-labelledby="storage-volumes">
      <PanelHead
        id="storage-volumes"
        name="Volumes"
        help={
          "The disks that hold database files, with their free space and when each fills at " +
          "the pace of the last week, from sys.dm_os_volume_stats, read every 5 minutes. " +
          "Amber from 85% used and red from 95%, the volume rule's levels. A full volume " +
          "stops its databases from growing, and writes fail."
        }
      >
        <span>that hold database files</span>
      </PanelHead>
      <ul className="live-plain">
        {data.volumes.map((volume) => (
          <li key={volume.volume}>
            <span>
              <b className="mono">{volume.volume}</b>{" "}
              <small className="faint">
                {formatAmount(volume.freeBytes)} free of {formatAmount(volume.sizeBytes)}
                {volume.daysUntilFull === null
                  ? " · not filling"
                  : ` · full in ${Math.round(volume.daysUntilFull)} days`}
              </small>
            </span>
            <span className={`live-time ${volume.usedRatio >= 0.95 ? "bad" : volume.usedRatio >= 0.85 ? "warn" : ""}`}>
              {Math.round(volume.usedRatio * 100)}%
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Props for {@link DatabaseLink}. */
interface DatabaseLinkProps {
  /** The database. */
  name: string;
  /** Whether the Advanced view is on, where the database's details are on the tab. */
  advanced: boolean;
}

/**
 * Leads from a finding to its database (VEY-62): in the Advanced view to its details, opened; in the Simple view to its
 * row in the list of databases.
 */
function DatabaseLink({ name, advanced }: DatabaseLinkProps) {
  function go() {
    if (advanced) {
      const detail = document.getElementById(databaseDetailId(name));

      if (detail instanceof HTMLDetailsElement) {
        detail.open = true;
      }

      showPart(databaseDetailId(name));

      return;
    }

    showPart(databaseRowId(name));
  }

  return (
    <button
      type="button"
      className="link-button"
      aria-label={advanced ? `See ${name}'s details` : `See ${name} in the list`}
      onClick={go}
    >
      See {name} ›
    </button>
  );
}

/** One database's details, folded until opened. */
function DatabaseDetail({ database }: { database: DatabaseStorage }) {
  const age = (seconds: number | null) => (seconds === null ? "never" : `${formatSpan(seconds)} ago`);

  return (
    <details className="storage-database db-drill-target" id={databaseDetailId(database.name)} tabIndex={-1}>
      <summary>
        <b>{database.name}</b>{" "}
        <small className="faint">
          {database.state.toLowerCase()} · {database.recoveryModel.toLowerCase()} recovery
          {database.findings.length > 0 ? ` · ${database.findings.length} to look at` : ""}
        </small>
      </summary>
      <dl className="live-facts">
        <dt>Settings</dt>
        <dd>
          compatibility {database.compatibility} · page verify {database.pageVerify.toLowerCase()} · auto close{" "}
          {database.autoClose ? "on" : "off"} · auto shrink {database.autoShrink ? "on" : "off"} · read committed
          snapshot {database.readCommittedSnapshot ? "on" : "off"} ·{" "}
          {database.encrypted ? "encrypted" : "not encrypted"}
        </dd>
        <dt>Log</dt>
        <dd>
          {database.logUsedRatio === null ? "–" : `${Math.round(database.logUsedRatio * 100)}% in use`} ·{" "}
          {database.virtualLogFiles ?? "–"} virtual log files · waits for{" "}
          {database.logReuseWait.replaceAll("_", " ").toLowerCase() || "nothing"}
        </dd>
        <dt>Backups</dt>
        <dd>
          full {age(database.fullBackupAgeSeconds)}
          {database.lastFullSeconds !== null && ` (took ${formatSpan(database.lastFullSeconds)}`}
          {database.lastFullBytes !== null && `, ${formatAmount(database.lastFullBytes)}`}
          {database.lastFullSeconds !== null && ")"} · differential {age(database.diffBackupAgeSeconds)} · log{" "}
          {database.logBackupAgeSeconds === null ? "not needed" : age(database.logBackupAgeSeconds)}
          {database.logChainBroken && " · the log chain has a gap"}
        </dd>
        {database.lastFullDestination && (
          <>
            <dt>Backed up to</dt>
            <dd className="mono">{database.lastFullDestination}</dd>
          </>
        )}
        <dt>Integrity</dt>
        <dd>
          last good CHECKDB {age(database.checkdbAgeSeconds)} · {database.damagedPages} damaged pages
        </dd>
      </dl>
      <table className="route-table">
        <thead>
          <tr>
            <th scope="col">File</th>
            <th scope="col">Type</th>
            <th scope="col">Volume</th>
            <th scope="col" className="number">
              Size
            </th>
            <th scope="col">Growth</th>
            <th scope="col">Maximum</th>
          </tr>
        </thead>
        <tbody>
          {database.files.map((file) => (
            <tr key={file.name}>
              <th scope="row">{file.name}</th>
              <td data-label="Type">{file.type}</td>
              <td data-label="Volume" className="mono">
                {file.volume}
              </td>
              <td data-label="Size" className="number">
                {formatAmount(file.sizeBytes)}
              </td>
              <td data-label="Growth">{file.growth}</td>
              <td data-label="Maximum">{file.maxSize}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}

/** Props for {@link FindingItem}. */
export interface FindingItemProps {
  /** The finding. */
  finding: PlanWarning;
  /** Where it leads, such as its database (VEY-62), after what to do. */
  children?: ReactNode;
}

/** A finding with what to do, the statement to copy, and where it leads. */
export function FindingItem({ finding, children }: FindingItemProps) {
  const [copied, setCopied] = useState(false);

  return (
    <li className={`live-finding ${finding.severity}`}>
      <i aria-hidden="true" />
      <div>
        <b>{finding.title}</b>
        <p>
          {finding.detail} {finding.fix}
        </p>
        {finding.script && (
          <div className="live-actions">
            <code className="live-text">{finding.script}</code>
            <button
              type="button"
              className="button small"
              onClick={() => void copyText(finding.script ?? "").then(setCopied)}
            >
              {copied ? "Copied" : "Copy the statement"}
            </button>
          </div>
        )}
        {children && <div className="live-actions">{children}</div>}
      </div>
    </li>
  );
}
