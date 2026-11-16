import { useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";

import { api } from "../../api/client";
import type { ChartLine, DatabaseQuery, PlanWarning, QueryPlan } from "../../api/types";
import { useViewMode, ViewToggle } from "../../app/ViewToggle";
import { LineChart } from "../LineChart";
import { formatSeconds } from "../metricFormat";
import { RangePicker, rangeFrom } from "../RangePicker";
import { useMetricData } from "../useMetricData";
import { copyText, programName, shortCount } from "./liveFormat";
import "../metrics.css";
import "./live.css";

/** The colours of a statement's plans, the latest first. */
const planColors = ["var(--warning-fg)", "var(--accent)", "var(--ok-fg)", "var(--metric-violet)", "var(--faint)"];

/**
 * One statement of a SQL Server (ADR 0020, area 4), opened from the Queries tab. The Simple view says what is wrong in
 * words, how to fix it, and who runs it; the Advanced view adds every figure per run, its plans side by side with their
 * operators and warnings, and the plan to download for SQL Server Management Studio.
 */
export function QueryScreen() {
  const { engine = "", server = "", hash = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeFrom(params.get("range"));
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const { data, notFound, error } = useMetricData(`query-${engine}-${server}-${hash}-${range}`, (signal) =>
    api.databaseQuery(engine, server, hash, range, signal),
  );
  const back = `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}?tab=queries&range=${range}`;

  return (
    <main className="metrics">
      <Link className="metrics-back" to={back}>
        ← {server} · Queries
      </Link>
      <div className="metrics-detail-head">
        <div>
          <h1>{data?.name ?? "Statement"}</h1>
          <ul className="metric-tags">
            {data?.database && <li>{data.database}</li>}
            <li className="mono">{hash}</li>
            {data && <li>{data.plans.length === 1 ? "1 plan" : `${data.plans.length} plans`}</li>}
          </ul>
        </div>
        <ViewToggle />
        <RangePicker value={range} onChange={(next) => setParams({ range: next }, { replace: true })} />
      </div>
      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `The statement could not be loaded: ${error}`}
        </p>
      )}
      {notFound && <p className="metrics-empty">The statement did not run within the last {range}.</p>}
      {data && <Body query={data} advanced={advanced} engine={engine} server={server} />}
    </main>
  );
}

/** Props for {@link Body}. */
interface BodyProps {
  /** The statement. */
  query: DatabaseQuery;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
}

/** The statement's page below its title. */
function Body({ query, advanced, engine, server }: BodyProps) {
  const [chosen, setChosen] = useState<string | null>(null);
  const plan = query.plans.find((one) => one.hash === chosen) ?? query.plans[0];
  const worst = query.findings[0];
  const totalCallers = query.callers.reduce((sum, caller) => sum + caller.samples, 0);

  return (
    <div className="live">
      {worst ? (
        <section className={`live-lead ${worst.severity}`} aria-label="What is wrong">
          <i aria-hidden="true" />
          <div>
            <h2>{worst.title}</h2>
            <p>{worst.detail}</p>
          </div>
        </section>
      ) : (
        <section className="live-lead ok" aria-label="What is wrong">
          <i aria-hidden="true" />
          <div>
            <h2>Nothing wrong shows in this statement&apos;s plans or history.</h2>
            <p>
              It ran {shortCount(query.totals.executions)} times over the last {query.range}, taking{" "}
              {formatSeconds(query.totals.perRunSeconds)} a run.
            </p>
          </div>
        </section>
      )}

      {advanced && (
        <section className="live-tiles" aria-label="Totals">
          <Stat label="Runs" value={shortCount(query.totals.executions)} />
          <Stat label="Total time" value={formatSeconds(query.totals.totalSeconds)} />
          <Stat label="Time per run" value={formatSeconds(query.totals.perRunSeconds)} />
          <Stat
            label="CPU per run"
            value={formatSeconds(query.totals.executions > 0 ? query.totals.cpuSeconds / query.totals.executions : 0)}
          />
          <Stat
            label="Reads per run"
            value={shortCount(
              query.totals.executions > 0 ? Math.round(query.totals.reads / query.totals.executions) : 0,
            )}
          />
          <Stat label="Plans" value={String(query.plans.length)} />
        </section>
      )}

      <section className="metric-panel" aria-labelledby="query-time">
        <div className="metric-panel-head">
          <h2 id="query-time">Time per run</h2>
          <span>{advanced ? "a line per plan" : "the colour changes where SQL Server changed its way"}</span>
        </div>
        <LineChart
          title="Time per run"
          format={(value) => formatSeconds(value)}
          series={query.plans.slice(0, planColors.length).map((one, index) => ({
            line: planLine(query, one.hash),
            color: planColors[index] ?? "var(--accent)",
            name: advanced ? `Plan ${one.hash}` : index === 0 ? "Now" : "Before",
          }))}
        />
      </section>

      {query.findings.length > 0 && (
        <section className="metric-panel" aria-labelledby="query-fix">
          <div className="metric-panel-head">
            <h2 id="query-fix">How to fix it</h2>
            <span>best first</span>
          </div>
          <ol className="live-findings">
            {query.findings.map((finding) => (
              <Fix key={`${finding.kind}-${finding.title}`} finding={finding} />
            ))}
          </ol>
        </section>
      )}

      {advanced && plan && (
        <section className="metric-panel" aria-labelledby="query-plans">
          <div className="metric-panel-head">
            <h2 id="query-plans">Plans</h2>
            {plan.captured && (
              <a
                className="live-link"
                href={`/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/plans/${encodeURIComponent(plan.hash)}`}
                download={`${plan.hash}.sqlplan`}
              >
                .sqlplan ↓
              </a>
            )}
          </div>
          <div className="live-filters" role="group" aria-label="Plans">
            {query.plans.map((one) => (
              <button
                key={one.hash}
                type="button"
                className={`live-chip ${one.hash === plan.hash ? "on" : ""}`}
                aria-pressed={one.hash === plan.hash}
                onClick={() => setChosen(one.hash)}
              >
                {one.hash} · {formatSeconds(one.perRunSeconds)} a run · {shortCount(Math.round(one.readsPerRun))} reads
              </button>
            ))}
          </div>
          <Operators plan={plan} />
        </section>
      )}

      {query.history.length > 0 && (
        <section className="metric-panel" aria-labelledby="query-history">
          <div className="metric-panel-head">
            <h2 id="query-history">History from Query Store</h2>
            <span>kept by SQL Server over restarts</span>
          </div>
          <LineChart
            title="Time per run, Query Store"
            format={(value) => formatSeconds(value)}
            series={[
              {
                line: {
                  label: "Time per run",
                  latest: query.history.at(-1)?.perRunSeconds ?? null,
                  points: query.history.map((interval) => ({ at: interval.start, value: interval.perRunSeconds })),
                },
                color: "var(--accent)",
                name: "Time per run",
              },
            ]}
          />
          {advanced && query.waits.length > 0 && (
            <ul className="live-plain">
              {query.waits.map((wait) => (
                <li key={wait.category}>
                  <span>Waited on {wait.category}</span>
                  <span className="live-time">{formatSeconds(wait.seconds)}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      <section className="metric-panel" aria-labelledby="query-callers">
        <div className="metric-panel-head">
          <h2 id="query-callers">Who runs it</h2>
          <span>from the live view</span>
        </div>
        {query.callers.length === 0 ? (
          <p className="metrics-empty">The live view did not see it run: it finishes within its 10 seconds.</p>
        ) : (
          <ul className="live-plain">
            {query.callers.map((caller) => (
              <li key={`${caller.program}-${caller.host}-${caller.login}`}>
                <span>
                  <b>{programName(caller.program)}</b>{" "}
                  <small className="faint">
                    {caller.host}
                    {advanced ? ` · ${caller.login}` : ""}
                  </small>
                </span>
                <span className="live-time">{Math.round((caller.samples / totalCallers) * 100)}%</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="metric-panel" aria-labelledby="query-text">
        <div className="metric-panel-head">
          <h2 id="query-text">The statement</h2>
        </div>
        <pre className="live-sql">{query.text}</pre>
      </section>
    </div>
  );
}

/** One number of the statement's totals. */
function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="live-tile">
      <span className="live-tile-label">{label}</span>
      <b>{value}</b>
    </div>
  );
}

/** One finding with what to do and the statement to copy. */
function Fix({ finding }: { finding: PlanWarning }) {
  const [copied, setCopied] = useState(false);

  return (
    <li className={`live-finding ${finding.severity}`}>
      <i aria-hidden="true" />
      <div>
        <b>{finding.title}</b>
        <p>{finding.fix}</p>
        {finding.script && (
          <div className="live-actions">
            <code className="live-text">{finding.script}</code>
            <button
              type="button"
              className="button small"
              onClick={() => void copyText(finding.script ?? "").then(setCopied)}
            >
              {copied ? "Copied" : "Copy"}
            </button>
          </div>
        )}
      </div>
    </li>
  );
}

/** A plan's operators in plan order, each with its share of the cost. */
function Operators({ plan }: { plan: QueryPlan }) {
  if (!plan.captured) {
    return <p className="metrics-empty">Veyra has not captured this plan yet: it reads plans every 15 minutes.</p>;
  }

  return (
    <ol className="query-operators">
      {plan.operators.map((operator, index) => (
        <li key={index} style={{ paddingLeft: `${operator.depth * 14}px` }}>
          <span>
            <b>{operator.physical}</b>
            {operator.target && <small className="faint"> {operator.target}</small>}
            {operator.warned && <span className="live-pill wait">warning</span>}
          </span>
          <span className="query-cost" aria-label={`${Math.round(operator.costShare * 100)}% of the cost`}>
            <i
              style={{ width: `${Math.round(operator.costShare * 100)}%` }}
              className={operator.costShare > 0.3 ? "hot" : undefined}
            />
          </span>
          <span className="number">{Math.round(operator.costShare * 100)}%</span>
        </li>
      ))}
    </ol>
  );
}

/** The time per run of one plan as a chart line: its value only where it ran most. */
function planLine(query: DatabaseQuery, hash: string): ChartLine {
  const points = query.points.map((point) => ({
    at: point.start,
    value: point.plan === hash ? point.perRunSeconds : null,
  }));

  return { label: hash, latest: points.findLast((point) => point.value !== null)?.value ?? null, points };
}
