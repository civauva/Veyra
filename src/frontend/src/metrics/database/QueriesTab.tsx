import { useState } from "react";
import { Link } from "react-router";

import { api } from "../../api/client";
import type { MetricRange, QueryRow, QuerySort, QueryStoreState } from "../../api/types";
import { formatSeconds } from "../metricFormat";
import { rangeSearch, rangeWords } from "../metricRange";
import { useMetricData } from "../useMetricData";
import { PanelHead } from "./helpNames";
import { copyText, shortCount } from "./liveFormat";
import { SystemHidden, SystemTag, useSystemShown } from "./SystemFilter";

/** Props for {@link QueriesTab}. */
export interface QueriesTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** The range. */
  range: MetricRange;
  /** Whether the Advanced view is on. */
  advanced: boolean;
}

/** What the Advanced view can rank the statements by; the server ranks them before it keeps the top 100 (VEY-66). */
const sorts: { key: QuerySort; label: string }[] = [
  { key: "totalTime", label: "Total time" },
  { key: "cpu", label: "CPU" },
  { key: "reads", label: "Reads" },
  { key: "runs", label: "Runs" },
  { key: "timePerRun", label: "Time per run" },
];

/**
 * A SQL Server's Queries tab (ADR 0020, area 4). The Simple view lists only the statements with something worth a
 * look, each named by what it does with its finding in words; the Advanced view ranks every statement by any measure.
 * A statement opens its page.
 */
export function QueriesTab({ engine, server, range, advanced }: QueriesTabProps) {
  const [chosen, setSort] = useState<QuerySort>("totalTime");
  const [systemShown] = useSystemShown();
  // The Simple view leads with the busiest statements; only the Advanced view ranks by another measure.
  const sort = advanced ? chosen : "totalTime";
  const { data, error } = useMetricData(
    `queries-${engine}-${server}-${range}`,
    (signal) => api.databaseQueries(engine, server, range, systemShown, sort, !advanced, signal),
    { choices: `${systemShown}-${sort}-${advanced}` },
  );

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The queries could not be loaded: {error}
      </p>
    ) : (
      <p className="metrics-empty">Reading the statements…</p>
    );
  }

  const off = data.queryStoreOff;
  const base = `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/queries/`;
  const link = (row: QueryRow) => `${base}${encodeURIComponent(row.queryHash)}?${rangeSearch(range)}`;

  const store = off.length > 0 && <QueryStoreOff states={off} />;
  // The server leaves out its own statements, such as SSMS reading the catalog, unless asked (VEY-64, VEY-66).
  const statements = data.statements;
  const hidden = <SystemHidden hidden={data.hiddenStatements} noun="statement" />;

  if (!advanced) {
    // The server keeps the Simple view to the statements with a finding, read before its cut to 100 (VEY-67).
    const worth = statements;
    const busiest = data.busiest;

    return (
      <>
        {store}
        <section className="metric-panel" aria-labelledby="queries-findings">
          <PanelHead
            id="queries-findings"
            name="Queries worth a look"
            help={
              "The statements in which Veyra found a problem over the range: a plan that got " +
              "slower, a missing index, a parameter of the wrong type, a spill to tempdb. Read " +
              "every minute from sys.dm_exec_query_stats, and every 15 minutes from the busiest " +
              "statements' plans. Each leads to how to fix it."
            }
          >
            <span>{rangeWords(range, "over")}</span>
          </PanelHead>
          {worth.length === 0 ? (
            <p className="metrics-empty">
              No statement shows a problem {rangeWords(range, "over")}. The busiest was{" "}
              {busiest ? <Link to={link(busiest)}>{busiest.name}</Link> : "nothing"}.
            </p>
          ) : (
            <ul className="live-findings">
              {worth.map((row) => (
                <li key={row.queryHash} className={`live-finding ${row.findings[0]?.severity ?? "caution"}`}>
                  <i aria-hidden="true" />
                  <div>
                    <b>{row.findings[0]?.title}</b>
                    <p>
                      {row.name}
                      {row.database ? ` in ${row.database}` : ""}: {row.findings[0]?.detail}
                    </p>
                    <Link className="live-link" to={link(row)}>
                      How to fix it ›
                    </Link>
                  </div>
                </li>
              ))}
            </ul>
          )}
          {hidden}
        </section>
      </>
    );
  }

  // Ranked by the server, so a statement outside the 100 slowest still shows when it reads or runs the most.
  const rows = statements;

  return (
    <>
      {store}
      <section className="metric-panel" aria-labelledby="queries-ranking">
        <PanelHead
          id="queries-ranking"
          name="Statements"
          help={
            "Every statement that ran over the range, ranked by the measure chosen, from " +
            "sys.dm_exec_query_stats, read every minute. Total is all runs together, Per run " +
            "the average, Reads the logical reads: 8 KB pages read from memory or disk. A " +
            "statement counts only while its plan stays in the cache."
          }
        >
          <span>{rangeWords(range, "over")}</span>
        </PanelHead>
        <div className="live-filters" role="group" aria-label="Rank by">
          {sorts.map((one) => (
            <button
              key={one.key}
              type="button"
              className={`live-chip ${sort === one.key ? "on" : ""}`}
              aria-pressed={sort === one.key}
              onClick={() => setSort(one.key)}
            >
              {one.label}
            </button>
          ))}
        </div>
        {rows.length === 0 ? (
          <p className="metrics-empty">No statement ran in this range, apart from Veyra&apos;s agent.</p>
        ) : (
          <div className="live-table-wrap">
            <table className="route-table live-table">
              <thead>
                <tr>
                  <th scope="col">Statement</th>
                  <th scope="col">Database</th>
                  <th scope="col" className="number">
                    Total
                  </th>
                  <th scope="col" className="number">
                    Runs
                  </th>
                  <th scope="col" className="number">
                    Per run
                  </th>
                  <th scope="col" className="number">
                    CPU
                  </th>
                  <th scope="col" className="number">
                    Reads
                  </th>
                  <th scope="col">Findings</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.queryHash}>
                    <th scope="row">
                      <Link to={link(row)}>{row.name}</Link>
                      {row.system && <SystemTag />}
                      <small className="live-text">{row.text}</small>
                    </th>
                    <td data-label="Database">{row.database ?? "–"}</td>
                    <td data-label="Total" className="number">
                      {formatSeconds(row.totalSeconds)}
                    </td>
                    <td data-label="Runs" className="number">
                      {shortCount(row.executions)}
                    </td>
                    <td data-label="Per run" className="number">
                      {formatSeconds(row.perRunSeconds)}
                    </td>
                    <td data-label="CPU" className="number">
                      {formatSeconds(row.cpuSeconds)}
                    </td>
                    <td data-label="Reads" className="number">
                      {shortCount(row.reads)}
                    </td>
                    <td data-label="Findings">
                      {row.findings.map((finding) => (
                        <span
                          key={finding.kind}
                          className={`live-pill ${finding.severity === "warning" ? "bad" : "wait"}`}
                        >
                          {finding.kind}
                        </span>
                      ))}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {hidden}
        {data.agentStatements > 0 && (
          <p className="database-agent-work">
            Not ranked: Veyra&apos;s agent, {data.agentStatements} statements, {formatSeconds(data.agentSeconds)} in
            all.
          </p>
        )}
      </section>
    </>
  );
}

/** The databases without Query Store, each with the statement that turns it on, for a DBA to run. */
function QueryStoreOff({ states }: { states: QueryStoreState[] }) {
  const [copied, setCopied] = useState<string | null>(null);

  return (
    <section className="metric-panel" aria-labelledby="queries-store">
      <PanelHead
        id="queries-store"
        name="Query Store is off"
        help={
          "Query Store keeps each statement's figures and plans inside its database, over " +
          "restarts; Veyra reads it every 15 minutes. These databases have it off, so a " +
          "statement's history ends once SQL Server drops its plan from the cache. A DBA " +
          "turns it on with the statement below."
        }
      >
        <span>
          {states.length === 1 ? "1 database" : `${states.length} databases`} keep no history of their statements
        </span>
      </PanelHead>
      <p className="metrics-empty">
        Without it, a statement&apos;s history ends when SQL Server forgets its plan. Veyra never changes a server; a
        DBA turns it on with one statement.
      </p>
      <ul className="live-plain">
        {states.map((state) => (
          <li key={state.database}>
            <b>{state.database}</b>
            {state.script && (
              <button
                type="button"
                className="button small"
                onClick={() =>
                  void copyText(state.script ?? "").then((done) => setCopied(done ? state.database : null))
                }
              >
                {copied === state.database ? "Copied" : "Copy the statement"}
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
