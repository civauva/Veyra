import { useState } from "react";

import type { ServerOverview } from "../../../api/types";
import { LineChart } from "../../../metrics/LineChart";
import { showPart } from "../../../metrics/database/drill";
import { HelpTip } from "../../../ui/HelpTip";
import { lineOver, SqlFigure, timeWords, waitClock } from "./sqlParts";
import { Waits } from "./SqlOverviewTab";

/** Props for {@link SqlBlockingTab}. */
export interface SqlBlockingTabProps {
  /** The overview read, which carries the chains of the latest live read. */
  overview: ServerOverview;
  /** Opens the Live tab. */
  onLive(): void;
}

/**
 * Blocking on a SQL Server (VEY-75, reference screenshot 3): the figures, the sessions at the head of each chain, the
 * tree under the chosen one with its statement, the waits, and blocked sessions over the range. The episodes and
 * deadlocks of the range follow below, from the tab every look shares.
 */
export function SqlBlockingTab({ overview, onLive }: SqlBlockingTabProps) {
  const chains = overview.blocking.chains;
  const [chosenId, setChosenId] = useState<number | null>(null);
  const chosen = chains.find((chain) => chain.headSessionId === chosenId) ?? chains[0];
  const blocked = chains.reduce((sum, chain) => sum + chain.blockedCount, 0);
  const longest = [...chains].sort((a, b) => (b.waitSeconds ?? 0) - (a.waitSeconds ?? 0))[0];

  return (
    <>
      <section className="sq-figures sq-figures-five" aria-label="Figures">
        <SqlFigure
          label="Blocking sessions"
          value={chains.length}
          detail="at the head of a chain now"
          tone={chains.length > 0 ? "bad" : undefined}
          help="Sessions that hold a lock others wait for, and wait for nothing themselves, in the latest live read."
          onOpen={() => showPart("sq-chains-card")}
        />
        <SqlFigure
          label="Blocked sessions"
          value={blocked}
          detail="waiting behind them"
          line={overview.blocking.blockedLine}
          tone={blocked > 0 ? "warn" : undefined}
          help="Sessions waiting for a lock now, directly or behind another blocked session, and over the range."
          onOpen={() => showPart(blocked > 0 ? "sq-tree-card" : "sq-blocked-range-card")}
        />
        <SqlFigure
          label="Deadlocks"
          value={overview.blocking.deadlocks}
          detail="in the range"
          help="Sessions SQL Server ended because each waited for the other. The list below shows each one's graph."
          onOpen={() => showPart("blocking-deadlocks-part")}
        />
        <SqlFigure
          label="Total wait time"
          value={timeWords(overview.waits.totalSeconds)}
          detail="idle waits left out"
          help="How long sessions waited in the range, all wait types together, idle waits left out."
          onOpen={() => showPart("sq-waits-card")}
        />
        <SqlFigure
          label="Largest blocker"
          value={longest ? `SPID ${longest.headSessionId}` : "—"}
          detail={longest ? `blocking for ${timeWords(longest.waitSeconds)}` : "nothing is blocked"}
          help="The head blocker whose chain has waited longest. Open it for its tree and its statement."
          onOpen={
            longest
              ? () => {
                  setChosenId(longest.headSessionId);
                  showPart("sq-tree-card");
                }
              : undefined
          }
        />
      </section>

      <div className="sq-row sq-row-main">
        <section id="sq-chains-card" className="cn-card" aria-labelledby="sq-chains" tabIndex={-1}>
          <header>
            <h2 id="sq-chains">Active blocking sessions</h2>
            <HelpTip label="Active blocking sessions">
              The session at the head of each chain in the latest live read: what it runs, what it waits for, and how
              many sessions wait behind it. Choose one to see its tree and statement.
            </HelpTip>
            <button type="button" className="link-button cn-meta" onClick={onLive}>
              Open Live →
            </button>
          </header>
          {chains.length === 0 ? (
            <p className="cn-empty">Nothing is blocked in the latest live read.</p>
          ) : (
            <div className="cn-table-scroll">
              <table className="cn-table sq-chains">
                <thead>
                  <tr>
                    <th scope="col">SPID</th>
                    <th scope="col">Database</th>
                    <th scope="col" className="wide">
                      Login
                    </th>
                    <th scope="col">Wait type</th>
                    <th scope="col" className="num">
                      Wait time
                    </th>
                    <th scope="col" className="num">
                      Blocked
                    </th>
                    <th scope="col" className="wide">
                      SQL text
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {chains.map((chain) => (
                    <tr
                      key={chain.headSessionId}
                      aria-selected={chain === chosen}
                      className={chain === chosen ? "chosen" : undefined}
                      onClick={() => setChosenId(chain.headSessionId)}
                    >
                      <td>
                        <button type="button" className="cn-row-pick" onClick={() => setChosenId(chain.headSessionId)}>
                          {chain.headSessionId}
                        </button>
                      </td>
                      <td>{chain.database ?? "—"}</td>
                      <td className="wide">{chain.login ?? "—"}</td>
                      <td className="mono">{chain.waitType ?? "—"}</td>
                      <td className="num mono">{waitClock(chain.waitSeconds)}</td>
                      <td className="num">{chain.blockedCount}</td>
                      <td className="wide mono sq-statement">{chain.statement ?? "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
        <section id="sq-tree-card" className="cn-card" aria-labelledby="sq-tree" tabIndex={-1}>
          <header>
            <h2 id="sq-tree">{chosen ? `Session ${chosen.headSessionId}` : "Blocking tree"}</h2>
            <HelpTip label="Blocking tree">
              The sessions the chosen head blocker holds up, with what they wait for and how long, and the statement it
              runs. Ending it releases them; Live has the statement to do that.
            </HelpTip>
          </header>
          {!chosen ? (
            <p className="cn-empty">Nothing is blocked.</p>
          ) : (
            <div className="sq-pad">
              <ul className="sq-tree mono">
                <li className="sq-tree-root">
                  {chosen.headSessionId} · {chosen.database ?? "?"} · blocking {timeWords(chosen.waitSeconds)}
                </li>
                {chosen.blocked.map((one) => (
                  <li key={one.sessionId}>
                    {one.sessionId}{" "}
                    <span className="sq-muted">
                      {one.waitType ?? ""} · {waitClock(one.waitSeconds)}
                    </span>
                  </li>
                ))}
                {chosen.blockedCount > chosen.blocked.length && (
                  <li className="sq-muted">+{chosen.blockedCount - chosen.blocked.length} more further down</li>
                )}
              </ul>
              <dl className="cn-kv">
                <div>
                  <dt>Login</dt>
                  <dd>{chosen.login ?? "—"}</dd>
                </div>
                <div>
                  <dt>Host</dt>
                  <dd>{chosen.host ?? "—"}</dd>
                </div>
                <div>
                  <dt>Blocked sessions</dt>
                  <dd>{chosen.blockedCount}</dd>
                </div>
              </dl>
              <h3 className="cn-section">Current SQL text</h3>
              <pre className="cn-code">{chosen.statement ?? "The statement was not captured."}</pre>
            </div>
          )}
        </section>
      </div>

      <div className="sq-row sq-row-main">
        <Waits overview={overview} />
        <section id="sq-blocked-range-card" className="cn-card" aria-labelledby="sq-blocked-range" tabIndex={-1}>
          <header>
            <h2 id="sq-blocked-range">Blocked over the range</h2>
            <HelpTip label="Blocked over the range">
              How many sessions were blocked at each step of the top bar&apos;s range.
            </HelpTip>
          </header>
          <div className="sq-chart">
            <LineChart
              title="Blocked sessions"
              format={(value) => String(Math.round(value))}
              height={170}
              series={[
                {
                  line: lineOver("Blocked sessions", overview.blocking.blockedLine, overview.from, overview.to),
                  color: "var(--error-fg)",
                  filled: true,
                },
              ]}
            />
          </div>
        </section>
      </div>
    </>
  );
}
