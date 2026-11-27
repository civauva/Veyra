import { useState } from "react";
import { useParams, useSearchParams } from "react-router";

import { api } from "../../api/client";
import type { ChartLine, DatabaseQuery, MetricRange, PlanWarning, QueryPlan } from "../../api/types";
import { ScreenBar } from "../../ui/ScreenBar";
import { useViewMode } from "../../ui/ViewToggle";
import { LineChart } from "../LineChart";
import { formatSeconds } from "../metricFormat";
import { rangeOf, rangeSearch, rangeWords, withRange } from "../metricRange";
import { RangePicker } from "../RangePicker";
import { useMetricData } from "../useMetricData";
import { PanelHead, TileName } from "./helpNames";
import { copyText, programName, shortCount } from "./liveFormat";
import "../metrics.css";
import "./live.css";

/** The colours of a statement's plans, the latest first. */
const planColors = ["var(--warning-fg)", "var(--accent)", "var(--ok-fg)", "var(--metric-violet)", "var(--faint)"];

/**
 * One statement of a SQL Server (ADR 0020, area 4), opened from the Queries tab. The Simple view says what is wrong in
 * words, how to fix it, and who runs it; the Advanced view adds every figure per run, its plans side by side with their
 * operators and warnings, and the plan to download for SQL Server Management Studio. Its bar leads back to the server's
 * Queries tab and the Metrics overview (VEY-62).
 */
export function QueryScreen() {
  const { engine = "", server = "", hash = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const range = rangeOf(params);
  const [mode] = useViewMode();
  const advanced = mode === "advanced";
  const { data, notFound, error } = useMetricData(`query-${engine}-${server}-${hash}-${range}`, (signal) =>
    api.databaseQuery(engine, server, hash, range, signal),
  );
  const serverPage = `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}`;
  const back = `${serverPage}?tab=queries&${rangeSearch(range)}`;

  return (
    <main className="metrics">
      <ScreenBar
        trail={[
          { label: "Metrics", to: `/metrics?${rangeSearch(range)}` },
          { label: "Database servers", to: `/metrics?${rangeSearch(range)}#metrics-databases` },
          { label: server, to: back },
          { label: "Statement" },
        ]}
        view
      >
        <RangePicker value={range} onChange={(next) => setParams(withRange(params, next), { replace: true })} />
      </ScreenBar>
      <div className="metrics-detail-head">
        <div>
          <h1>{data?.name ?? "Statement"}</h1>
          <ul className="metric-tags">
            {data?.database && <li>{data.database}</li>}
            <li className="mono">{hash}</li>
            {data && <li>{data.plans.length === 1 ? "1 plan" : `${data.plans.length} plans`}</li>}
          </ul>
        </div>
      </div>
      {error && (
        <p className="metrics-error" role="alert">
          {data ? `Could not refresh: ${error}.` : `The statement could not be loaded: ${error}`}
        </p>
      )}
      {notFound && <p className="metrics-empty">The statement did not run {rangeWords(range, "within")}.</p>}
      {data && <Body query={data} range={range} advanced={advanced} engine={engine} server={server} />}
    </main>
  );
}

/** Props for {@link Body}. */
interface BodyProps {
  /** The statement. */
  query: DatabaseQuery;
  /** The range the screen shows, for the page's words. */
  range: MetricRange;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
}

/** The statement's page below its title. */
function Body({ query, range, advanced, engine, server }: BodyProps) {
  const [chosen, setChosen] = useState<string | null>(null);
  const plan = query.plans.find((one) => one.hash === chosen) ?? query.plans[0];
  const worst = query.findings[0];
  const totalCallers = query.callerSamples;

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
              It ran {shortCount(query.totals.executions)} times {rangeWords(range, "over")}, taking{" "}
              {formatSeconds(query.totals.perRunSeconds)} a run.
            </p>
          </div>
        </section>
      )}

      {advanced && (
        <section className="live-tiles" aria-label="Totals">
          <Stat
            label="Runs"
            help={
              "How many times the statement ran over the range, from execution_count in " +
              "sys.dm_exec_query_stats, read every minute. Many cheap runs can cost more in all " +
              "than one slow run."
            }
            value={shortCount(query.totals.executions)}
          />
          <Stat
            label="Total time"
            help={
              "The elapsed time of all its runs together over the range. The statements with " +
              "the most total time are where tuning pays off most."
            }
            value={formatSeconds(query.totals.totalSeconds)}
          />
          <Stat
            label="Time per run"
            help={
              "The average elapsed time of one run over the range, waiting included. Much more " +
              "than CPU per run means it spends its time waiting: on locks, the disk, or " +
              "memory."
            }
            value={formatSeconds(query.totals.perRunSeconds)}
          />
          <Stat
            label="CPU per run"
            help={
              "The processor time of one run on average. Close to time per run means the " +
              "statement works rather than waits; reading less is what lowers it."
            }
            value={formatSeconds(query.totals.executions > 0 ? query.totals.cpuSeconds / query.totals.executions : 0)}
          />
          <Stat
            label="Reads per run"
            help={
              "The logical reads of one run on average: 8 KB pages read from memory or disk. " +
              "Far more pages than the rows it returns usually means a scan that an index would " +
              "avoid."
            }
            value={shortCount(
              query.totals.executions > 0 ? Math.round(query.totals.reads / query.totals.executions) : 0,
            )}
          />
          <Stat
            label="Plans"
            help={
              "How many plans SQL Server used for the statement over the range. More than one " +
              "can mean its speed depends on the values of the run that compiled the plan, " +
              "known as parameter sensitivity."
            }
            value={String(query.plans.length)}
          />
        </section>
      )}

      <section className="metric-panel" aria-labelledby="query-time">
        <PanelHead
          id="query-time"
          name="Time per run"
          help={
            "The average time of one run over the range, from sys.dm_exec_query_stats, read " +
            "every minute. The colour changes where SQL Server changed its plan, the usual " +
            "reason a statement suddenly gets slower; Advanced draws a line per plan."
          }
        >
          <span>{advanced ? "a line per plan" : "the colour changes where SQL Server changed its way"}</span>
        </PanelHead>
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
          <PanelHead
            id="query-fix"
            name="How to fix it"
            help={
              "What Veyra found in the statement's plans and history, the best fix first: a " +
              "missing index, a parameter of the wrong type, a spill to tempdb, a plan that got " +
              "slower. A DBA reviews a statement to copy before running it."
            }
          >
            <span>best first</span>
          </PanelHead>
          <ol className="live-findings">
            {query.findings.map((finding) => (
              <Fix key={`${finding.kind}-${finding.title}`} finding={finding} />
            ))}
          </ol>
        </section>
      )}

      {advanced && plan && (
        <section className="metric-panel" aria-labelledby="query-plans">
          <PanelHead
            id="query-plans"
            name="Plans"
            help={
              "Each plan SQL Server used, with its operators in plan order and each one's share " +
              "of the estimated cost. Captured every 15 minutes for the 10 busiest statements. " +
              "Start with the costliest operator, above all a scan of a large table; the " +
              ".sqlplan opens in SQL Server Management Studio."
            }
          >
            {plan.captured && (
              <a
                className="live-link"
                href={`/api/v1/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}/plans/${encodeURIComponent(plan.hash)}`}
                download={`${plan.hash}.sqlplan`}
              >
                .sqlplan ↓
              </a>
            )}
          </PanelHead>
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
          <PanelHead
            id="query-history"
            name="History from Query Store"
            help={
              "The statement's time per run as Query Store kept it inside the database, over " +
              "restarts and after its plan left the cache, read every 15 minutes. Advanced " +
              "lists what it waited on most. A step up in the line is the moment to look at."
            }
          >
            <span>kept by SQL Server over restarts</span>
          </PanelHead>
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
        <PanelHead
          id="query-callers"
          name="Who runs it"
          help={
            "The programs and machines seen running the statement, each by its share of the " +
            "Live tab's reads every 10 seconds that caught it. A statement that always " +
            "finishes within 10 seconds may not be caught at all."
          }
        >
          <span>from the live view</span>
        </PanelHead>
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
        <PanelHead
          id="query-text"
          name="The statement"
          help={
            "The statement's text as SQL Server reports it, which the other figures on this " +
            "page measure. It can hold values from the application, so share it with care."
          }
        />
        <pre className="live-sql">{query.text}</pre>
      </section>
    </div>
  );
}

/** Props for {@link Stat}. */
interface StatProps {
  /** What the number is. */
  label: string;
  /** What the number means, behind the question mark beside its name (VEY-60). */
  help: string;
  /** The number, formatted. */
  value: string;
}

/** One number of the statement's totals, with a question mark that explains it. */
function Stat({ label, help, value }: StatProps) {
  return (
    <div className="live-tile">
      <TileName name={label} help={help} className="live-tile-label" />
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
