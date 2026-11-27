import { api } from "../../api/client";
import { unreadText } from "./unread";
import { formatAmount } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { PanelHead } from "./helpNames";
import { shortCount } from "./liveFormat";
import { FindingItem } from "./StorageTab";
import { SystemHidden, useSystemShown } from "./SystemFilter";

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
  const [systemShown] = useSystemShown();
  // The server leaves the system databases out of the advice and the list unless System is on (VEY-66), and keeps the
  // Simple view to the five worst findings (VEY-67).
  const { data } = useMetricData(
    `indexes-${engine}-${server}`,
    (signal) => api.databaseIndexes(engine, server, systemShown, !advanced, signal),
    { choices: `${systemShown}-${advanced}` },
  );

  if (!data) {
    return null;
  }

  return (
    <section className="metric-panel" aria-labelledby="indexes-head">
      <PanelHead
        id="indexes-head"
        name="Indexes and statistics"
        help={
          "Advice read every hour from the missing index views, " +
          "sys.dm_db_index_usage_stats, and the statistics of tables over 100,000 rows: " +
          "indexes worth adding, unused or duplicate ones that only cost writes, heaps, " +
          "stale statistics, and lock escalations, each with the statement to run. Use " +
          "counts start when SQL Server starts, so judge them after it has run a while."
        }
      >
        <span>read every hour</span>
      </PanelHead>
      {data.findings.length === 0 ? (
        <p className="metrics-empty">
          {blind ? unreadText : "No index or statistics advice: nothing missing, unused, or stale stands out."}
        </p>
      ) : (
        <ul className="live-findings">
          {data.findings.map((finding, index) => (
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
      {advanced && <SystemHidden hidden={data.hiddenIndexes} noun="index" plural="indexes" />}
    </section>
  );
}
