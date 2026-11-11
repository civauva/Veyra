import { Link } from "react-router";

import type { AlertSeverity, HomeSnapshot } from "../../api/types";
import { formatDays, formatDiskShare, notFillingDays } from "../../alerts/ruleWords";

/** Props for {@link DisksPanel}. */
interface DisksPanelProps {
  /** The latest snapshot. */
  snapshot: HomeSnapshot;
}

/**
 * Every watched disk, fullest first (VEY-24): how full it is, whether it is filling, and a link to its host's
 * metrics. A disk is lit only by an open disk alert on it, as every block on the home is lit only by alerts.
 */
export function DisksPanel({ snapshot }: DisksPanelProps) {
  const disks = [...snapshot.disks].sort((a, b) => b.ratio - a.ratio);

  return (
    <section className="glass lg-panel lg-disks" aria-label="Disks">
      <div className="lg-head">
        <h2 className="cap">Disks · fullest first</h2>
        {disks.length > 0 && <span className="lg-count">{disks.length} watched</span>}
      </div>
      {disks.length === 0 ? (
        <p className="fine">Disks are watched with host metrics from the OpenTelemetry Collector.</p>
      ) : (
        <ul className="lg-disk-rows">
          {disks.map((disk) => {
            const severity = severityOf(snapshot, disk.host, disk.mountpoint);
            const filling = disk.daysUntilFull !== null && disk.daysUntilFull < notFillingDays;

            return (
              <li key={`${disk.host}${disk.mountpoint}`}>
                <Link
                  className={`lg-disk ${severity === "Warning" ? "w" : severity === "Caution" ? "c" : ""}`}
                  to={`/metrics/hosts/${encodeURIComponent(disk.host)}`}
                >
                  <span className="lg-disk-name">
                    <b>{disk.mountpoint}</b>
                    <small>{disk.host}</small>
                  </span>
                  <span className="lg-disk-bar" aria-hidden="true">
                    <i style={{ width: `${Math.min(Math.max(disk.ratio, 0), 1) * 100}%` }} />
                  </span>
                  <span className="lg-disk-value">{formatDiskShare(disk.ratio)}</span>
                  <small className="lg-disk-note">
                    {filling ? `full in ${formatDays(disk.daysUntilFull ?? 0)}` : "steady"}
                  </small>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** The severity of the open disk alert on this disk, if any. */
function severityOf(snapshot: HomeSnapshot, host: string, mountpoint: string): AlertSeverity | null {
  const lit = snapshot.alerts.filter(
    (alert) =>
      alert.kind === "host" &&
      alert.name === host &&
      alert.part === mountpoint &&
      (alert.rule === "disk-space" || alert.rule === "disk-filling"),
  );

  return lit.some((alert) => alert.severity === "Warning") ? "Warning" : lit.length > 0 ? "Caution" : null;
}
