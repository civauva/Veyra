import { api } from "../../api/client";
import { unreadText } from "./unread";
import { formatAmount } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { shortCount } from "./liveFormat";
import { FindingItem } from "./StorageTab";

/** Props for {@link IndexesSection}. */
export interface IndexesSectionProps {
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
 * Index and statistics advice of a SQL Server (ADR 0020, area 9): missing, unused, and duplicate indexes, heaps, stale
 * statistics, and lock escalations, each with the statement to run; the Advanced view adds the largest indexes and
 * their use.
 */
export function IndexesSection({ engine, server, advanced, blind = false }: IndexesSectionProps) {
  const { data } = useMetricData(`indexes-${engine}-${server}`, (signal) =>
    api.databaseIndexes(engine, server, signal),
  );

  if (!data) {
    return null;
  }

  return (
    <section className="metric-panel" aria-labelledby="indexes-head">
      <div className="metric-panel-head">
        <h2 id="indexes-head">Indexes and statistics</h2>
        <span>read every hour</span>
      </div>
      {data.findings.length === 0 ? (
        <p className="metrics-empty">
          {blind ? unreadText : "No index or statistics advice: nothing missing, unused, or stale stands out."}
        </p>
      ) : (
        <ul className="live-findings">
          {(advanced ? data.findings : data.findings.slice(0, 5)).map((finding, index) => (
            <FindingItem key={`${finding.kind}-${index}`} finding={finding} />
          ))}
        </ul>
      )}
      {advanced && data.indexes.length > 0 && (
        <div className="live-table-wrap">
          <table className="route-table">
            <thead>
              <tr>
                <th scope="col">Index</th>
                <th scope="col">Keys</th>
                <th scope="col" className="number">
                  Size
                </th>
                <th scope="col" className="number">
                  Reads
                </th>
                <th scope="col" className="number">
                  Writes
                </th>
                <th scope="col" className="number">
                  Escalations
                </th>
              </tr>
            </thead>
            <tbody>
              {data.indexes.map((index) => (
                <tr key={`${index.database}-${index.table}-${index.name}`}>
                  <th scope="row">
                    {index.name || "(heap)"}{" "}
                    <small className="faint">
                      {index.database} · {index.table}
                    </small>
                  </th>
                  <td data-label="Keys" className="mono">
                    {index.keys}
                    {index.include ? ` + ${index.include}` : ""}
                  </td>
                  <td data-label="Size" className="number">
                    {formatAmount(index.sizeBytes)}
                  </td>
                  <td data-label="Reads" className="number">
                    {shortCount(index.reads)}
                  </td>
                  <td data-label="Writes" className="number">
                    {shortCount(index.writes)}
                  </td>
                  <td data-label="Escalations" className="number">
                    {shortCount(index.escalations)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
