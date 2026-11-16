import { useState } from "react";

import { api } from "../../api/client";
import type { DatabaseStorage, PlanWarning, StorageView } from "../../api/types";
import { formatAmount, formatSpan } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { copyText } from "./liveFormat";

/** Props for {@link StorageTab}. */
export interface StorageTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/**
 * The storage part of a SQL Server's Databases tab (ADR 0020, areas 6 to 8): what needs doing, in words with the
 * statement to run; the volumes and when they fill; and in the Advanced view each database's settings, files, log,
 * backups, and integrity.
 */
export function StorageTab({ engine, server, advanced }: StorageTabProps) {
  const { data, notFound, error } = useMetricData(`storage-${engine}-${server}`, (signal) =>
    api.databaseStorage(engine, server, signal),
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

  const findings = [...data.findings, ...data.databases.flatMap((database) => database.findings)].sort((a, b) =>
    a.severity === b.severity ? 0 : a.severity === "warning" ? -1 : 1,
  );

  return (
    <>
      <section className="metric-panel" aria-labelledby="storage-findings">
        <div className="metric-panel-head">
          <h2 id="storage-findings">Needs doing</h2>
          <span>files, logs, backups, and integrity</span>
        </div>
        {findings.length === 0 ? (
          <p className="metrics-empty">Nothing: files, logs, backups, and integrity checks all look right.</p>
        ) : (
          <ul className="live-findings">
            {findings.map((finding, index) => (
              <FindingItem key={`${finding.kind}-${index}`} finding={finding} />
            ))}
          </ul>
        )}
      </section>

      <Volumes data={data} />

      {advanced && (
        <section className="metric-panel" aria-labelledby="storage-detail">
          <div className="metric-panel-head">
            <h2 id="storage-detail">Each database</h2>
            <span>settings, files, log, backups, integrity</span>
          </div>
          {data.databases.map((database) => (
            <DatabaseDetail key={database.name} database={database} />
          ))}
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
      <div className="metric-panel-head">
        <h2 id="storage-volumes">Volumes</h2>
        <span>that hold database files</span>
      </div>
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

/** One database's details, folded until opened. */
function DatabaseDetail({ database }: { database: DatabaseStorage }) {
  const age = (seconds: number | null) => (seconds === null ? "never" : `${formatSpan(seconds)} ago`);

  return (
    <details className="storage-database">
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

/** A finding with what to do and the statement to copy. */
export function FindingItem({ finding }: { finding: PlanWarning }) {
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
      </div>
    </li>
  );
}
