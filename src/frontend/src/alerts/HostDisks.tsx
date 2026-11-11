import type { AlertDetails, AlertDisk } from "../api/types";
import { formatDays, formatDiskShare } from "./ruleWords";

/** Props for {@link HostDisks}. */
interface HostDisksProps {
  /** The disk alert. */
  alert: AlertDetails;
  /** Its disk, with the host's other disks. */
  disk: AlertDisk;
}

/**
 * Every disk of the alert's host as it is now, fullest first, with the alert's own disk marked, so a person sees
 * whether it is one disk or the whole host.
 */
export function HostDisks({ alert, disk }: HostDisksProps) {
  return (
    <section className="glass alert-panel" aria-labelledby="alert-host-disks">
      <h2 id="alert-host-disks" className="cap">
        Disks of {alert.block.name}
      </h2>
      {disk.disks.length === 0 ? (
        <p className="fine">{alert.block.name} sent no disk figures in the last 15 minutes.</p>
      ) : (
        <ul className="host-disks">
          {disk.disks.map((other) => {
            const own = other.mountpoint === disk.mountpoint;
            const filling = other.daysUntilFull !== null;

            return (
              <li key={other.mountpoint} className={own ? "host-disk own" : "host-disk"}>
                <code>{other.mountpoint}</code>
                <span className="disk-bar" aria-hidden="true">
                  <i
                    className={own || other.ratio >= 0.9 ? "hot" : ""}
                    style={{ width: `${Math.min(100, other.ratio * 100)}%` }}
                  />
                </span>
                <small>
                  {formatDiskShare(other.ratio)} ·{" "}
                  {filling ? `full in ${formatDays(other.daysUntilFull ?? 0)}` : "steady"}
                </small>
              </li>
            );
          })}
        </ul>
      )}
      <p className="fine">Each disk is watched on its own: for the share in use, and for how soon it is full.</p>
    </section>
  );
}
