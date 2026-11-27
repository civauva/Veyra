import { api } from "../../api/client";
import type { Deadlock, MetricRange } from "../../api/types";
import { formatClockTime } from "../metricFormat";
import { rangeWords } from "../metricRange";
import { useMetricData } from "../useMetricData";
import { PanelHead } from "./helpNames";
import { clock, programName } from "./liveFormat";

/** Props for {@link BlockingTab}. */
export interface BlockingTabProps {
  /** The engine. */
  engine: string;
  /** The server. */
  server: string;
  /** The range. */
  range: MetricRange;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** Opens the Live tab at a moment. */
  onMoment: (at: string) => void;
}

/**
 * A SQL Server's Blocking tab (ADR 0020, area 5): the blocking episodes and deadlocks over the range. The Simple view
 * tells each as a sentence; the Advanced view adds the head blocker's statement and each deadlock's sessions and
 * locked resources. An episode opens the Live tab at the moment it began.
 */
export function BlockingTab({ engine, server, range, advanced, onMoment }: BlockingTabProps) {
  const { data, error } = useMetricData(`blocking-${engine}-${server}-${range}`, (signal) =>
    api.databaseBlocking(engine, server, range, signal),
  );

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        Blocking could not be loaded: {error}
      </p>
    ) : (
      <p className="metrics-empty">Reading blocking…</p>
    );
  }

  return (
    <div className="metric-panels">
      <section className="metric-panel" aria-labelledby="blocking-episodes">
        <PanelHead
          id="blocking-episodes"
          name="Blocking"
          help={
            "The times one session held up others over the range. Veyra builds them from the " +
            "Live tab's reads every 10 seconds: the same head blocker in reads at most 35 " +
            "seconds apart is one episode. Each opens the Live tab at the moment it began, to " +
            "see who held what."
          }
        >
          <span>{rangeWords(range, "over")}</span>
        </PanelHead>
        {data.episodes.length === 0 ? (
          <p className="metrics-empty">Nothing waited for anything {rangeWords(range, "over")}.</p>
        ) : (
          <ul className="live-findings">
            {data.episodes.map((episode) => (
              <li
                key={`${episode.start}-${episode.head}`}
                className={`live-finding ${episode.longestWaitSeconds >= 60 ? "warning" : "caution"}`}
              >
                <i aria-hidden="true" />
                <div>
                  <b>
                    {formatClockTime(episode.start)} · {episode.summary}
                  </b>
                  {advanced && (
                    <p>
                      Session {episode.head} ({episode.sleeping ? "sleeping, open transaction" : "running"}) ·{" "}
                      {episode.mostWaiting} waiting at most · longest {clock(episode.longestWaitSeconds)} ·{" "}
                      <code className="live-text">{episode.headText}</code>
                    </p>
                  )}
                  <button type="button" className="link-button" onClick={() => onMoment(episode.start)}>
                    See it in the Live tab ›
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section id="blocking-deadlocks-part" className="metric-panel" aria-labelledby="blocking-deadlocks" tabIndex={-1}>
        <PanelHead
          id="blocking-deadlocks"
          name="Deadlocks"
          help={
            "Deadlocks recorded by SQL Server's built-in system_health session, read every " +
            "minute. Two sessions each waited for the other, so SQL Server ended one, the " +
            "victim, whose application got error 1205. The same pair again and again usually " +
            "needs an index or the same order of access in both."
          }
        >
          <span>from SQL Server&apos;s system_health session</span>
        </PanelHead>
        {data.deadlocks.length === 0 ? (
          <p className="metrics-empty">No deadlock {rangeWords(range, "over")}.</p>
        ) : (
          <ul className="live-findings">
            {data.deadlocks.map((deadlock) => (
              <DeadlockItem key={deadlock.key} deadlock={deadlock} advanced={advanced} />
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

/** One deadlock: its sentence, and in the Advanced view its sessions and resources side by side. */
function DeadlockItem({ deadlock, advanced }: { deadlock: Deadlock; advanced: boolean }) {
  return (
    <li className="live-finding warning">
      <i aria-hidden="true" />
      <div>
        <b>
          {formatClockTime(deadlock.at)} · {deadlock.summary}
        </b>
        <div className="deadlock-graph">
          {deadlock.processes.map((process) => (
            <div key={process.id} className={`live-link-card ${process.victim ? "head" : ""}`}>
              <b>
                {programName(process.program)}
                {process.victim ? " · ended by SQL Server" : ""}
              </b>
              <small>
                {process.host} · session {process.session}
                {advanced ? ` · ${process.login} · ${process.database}` : ""}
              </small>
              <span>{process.name}</span>
              {advanced && <small className="mono">waited for {process.waits}</small>}
              {advanced && <code className="live-text">{process.text}</code>}
            </div>
          ))}
        </div>
        {advanced && deadlock.resources.length > 0 && (
          <ul className="deadlock-resources">
            {deadlock.resources.map((resource, index) => (
              <li key={index}>
                <b>
                  {resource.kind} {resource.target}
                </b>{" "}
                held by {resource.holders.join(", ") || "–"}; wanted by {resource.waiters.join(", ") || "–"}
              </li>
            ))}
          </ul>
        )}
      </div>
    </li>
  );
}
