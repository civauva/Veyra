import { api } from "../../api/client";
import { unreadText } from "./unread";
import { formatClockTime } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { FindingItem } from "./StorageTab";

/** Props for {@link ConfigurationSection}. */
export interface ConfigurationSectionProps {
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
 * A SQL Server's configuration and security posture on the Server tab (ADR 0020, area 13): what to change, with the
 * statement to run; the Advanced view adds every setting worth knowing, the sysadmin members, and the changes of the
 * last 30 days.
 */
export function ConfigurationSection({ engine, server, advanced, blind = false }: ConfigurationSectionProps) {
  const { data } = useMetricData(`configuration-${engine}-${server}`, (signal) =>
    api.databaseConfiguration(engine, server, signal),
  );

  if (!data || (data.settings.every((setting) => setting.value === "") && data.findings.length === 0)) {
    return null;
  }

  return (
    <section className="metric-panel" aria-labelledby="configuration-head">
      <div className="metric-panel-head">
        <h2 id="configuration-head">Configuration and security</h2>
        <span>read every hour</span>
      </div>
      {data.findings.length === 0 ? (
        <p className="metrics-empty">{blind ? unreadText : "The settings and security posture look right."}</p>
      ) : (
        <ul className="live-findings">
          {data.findings.map((finding, index) => (
            <FindingItem key={`${finding.kind}-${index}`} finding={finding} />
          ))}
        </ul>
      )}
      {advanced && (
        <>
          <table className="route-table">
            <thead>
              <tr>
                <th scope="col">Setting</th>
                <th scope="col">Value</th>
                <th scope="col">Recommended</th>
              </tr>
            </thead>
            <tbody>
              {data.settings.map((setting) => (
                <tr key={setting.name}>
                  <th scope="row">{setting.name}</th>
                  <td data-label="Value">{setting.value || "–"}</td>
                  <td data-label="Recommended">{setting.recommended}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="metrics-empty">sysadmin: {data.sysadmins.join(", ") || "–"}</p>
          {data.changes.length > 0 && (
            <ul className="live-plain">
              {data.changes.map((change) => (
                <li key={`${change.at}-${change.item}-${change.to}`}>
                  <span>
                    <b>{formatClockTime(change.at)}</b>{" "}
                    {change.item === "sysadmin"
                      ? `${change.to} joined sysadmin`
                      : `${change.item}: ${change.from} → ${change.to}`}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
