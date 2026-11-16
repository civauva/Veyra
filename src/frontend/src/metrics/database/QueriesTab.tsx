import { useState } from "react";
import { Link } from "react-router";

import { api } from "../../api/client";
import type { MetricRange, QueryRow, QueryStoreState } from "../../api/types";
import { formatSeconds } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { copyText, shortCount } from "./liveFormat";

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

/** What the Advanced view ranks the statements by. */
type SortKey = "totalSeconds" | "cpuSeconds" | "reads" | "executions" | "perRunSeconds";

const sorts: { key: SortKey; label: string }[] = [
  { key: "totalSeconds", label: "Total time" },
  { key: "cpuSeconds", label: "CPU" },
  { key: "reads", label: "Reads" },
  { key: "executions", label: "Runs" },
  { key: "perRunSeconds", label: "Time per run" },
];

/**
 * A SQL Server's Queries tab (ADR 0020, area 4). The Simple view lists only the statements with something worth a
 * look, each named by what it does with its finding in words; the Advanced view ranks every statement by any measure.
 * A statement opens its page.
 */
export function QueriesTab({ engine, server, range, advanced }: QueriesTabProps) {
  const { data, error } = useMetricData(`queries-${engine}-${server}-${range}`, (signal) =>
    api.databaseQueries(engine, server, range, signal),
  );
  const [sort, setSort] = useState<SortKey>("totalSeconds");

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The queries could not be loaded: {error}
      </p>
    ) : (
      <p className="metrics-empty">Reading the statements…</p>
    );
  }

  const off = data.queryStore.filter((state) => !state.on);
  const base = `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/queries/`;
  const link = (row: QueryRow) => `${base}${encodeURIComponent(row.queryHash)}?range=${range}`;

  const store = off.length > 0 && <QueryStoreOff states={off} />;

  if (!advanced) {
    const worth = data.statements.filter((row) => row.findings.length > 0);

    return (
      <>
        {store}
        <section className="metric-panel" aria-labelledby="queries-findings">
          <div className="metric-panel-head">
            <h2 id="queries-findings">Queries worth a look</h2>
            <span>over the last {data.range}</span>
          </div>
          {worth.length === 0 ? (
            <p className="metrics-empty">
              No statement shows a problem over the last {data.range}. The busiest was{" "}
              {data.statements[0] ? <Link to={link(data.statements[0])}>{data.statements[0].name}</Link> : "nothing"}.
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
        </section>
      </>
    );
  }

  const rows = [...data.statements].sort((a, b) => b[sort] - a[sort]);

  return (
    <>
      {store}
      <section className="metric-panel" aria-labelledby="queries-ranking">
        <div className="metric-panel-head">
          <h2 id="queries-ranking">Statements</h2>
          <span>over the last {data.range}</span>
        </div>
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
      <div className="metric-panel-head">
        <h2 id="queries-store">Query Store is off</h2>
        <span>
          {states.length === 1 ? "1 database" : `${states.length} databases`} keep no history of their statements
        </span>
      </div>
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
