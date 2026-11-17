import { useCallback, useContext, useMemo, useRef, useState } from "react";

import { api } from "../../api/client";
import type { BlockingNode, DatabaseLive, Finding, LiveSession, WaitTerm } from "../../api/types";
import { formatAmount, formatClockTime, formatRate, formatShare } from "../metricFormat";
import { useMetricData } from "../useMetricData";
import { chainsMeeting, meets, useLiveFilters, type FilterKind, type LiveFilters } from "./liveFilters";
import { clock, doingWords, programName, shortCount } from "./liveFormat";
import { FilterChip, FindingActions, LiveTile, WaitPill } from "./liveParts";
import { SelectedSession } from "./selectedSession";
import { SessionPanel } from "./SessionPanel";

/** How often the Live tab asks again: the agent reads every 10 seconds (ADR 0020). */
export const liveRefreshMilliseconds = 10_000;

/** Props for {@link LiveTab}. */
export interface LiveTabProps {
  /** The engine from the address. */
  engine: string;
  /** The server from the address. */
  server: string;
  /** A moment to look back at, from the address, or null for now. */
  at: string | null;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** Goes back to now from a moment looked back at. */
  onNow: () => void;
  /** Switches to the Advanced view, from the Simple view's link to the evidence. */
  onAdvanced: () => void;
}

/**
 * A SQL Server's Live tab (ADR 0020, area 3): what runs, waits, and blocks now, refreshed every 10 seconds, or a
 * moment of the last days. The Simple view leads with Veyra's findings in words about programs and machines; the
 * Advanced view shows the blocking tree, every column of every session, and SQL Server's own terms. Every value opens
 * its detail, filters the page, or explains itself.
 */
export function LiveTab({ engine, server, at, advanced, onNow, onAdvanced }: LiveTabProps) {
  const { data, notFound, error } = useMetricData(
    `live-${engine}-${server}-${at ?? "now"}`,
    (signal) => api.databaseLive(engine, server, at, signal),
    { refreshMs: liveRefreshMilliseconds, paused: at !== null },
  );
  const filters = useLiveFilters();
  const [selected, setSelected] = useState<number | null>(null);
  const opener = useRef<HTMLElement | null>(null);

  const open = useCallback((id: number) => {
    opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setSelected(id);
  }, []);

  const close = useCallback(() => {
    setSelected(null);
    opener.current?.focus();
  }, []);

  if (notFound) {
    return <p className="metrics-empty">{server} has sent nothing Veyra can show live.</p>;
  }

  if (!data) {
    return error ? (
      <p className="metrics-error" role="alert">
        The live view could not be loaded: {error}
      </p>
    ) : (
      <p className="metrics-empty">Reading the server…</p>
    );
  }

  const session = selected === null ? undefined : data.sessions.find((one) => one.id === selected);

  return (
    <div className={`live-screen ${session ? "with-panel" : ""}`}>
      <LiveHeader live={data} error={error} onNow={onNow} />
      <div className="live-main">
        <SelectedSession.Provider value={session ? session.id : null}>
          {advanced ? (
            <Advanced live={data} filters={filters} onSession={open} />
          ) : (
            <Simple live={data} filters={filters} onSession={open} onAdvanced={onAdvanced} />
          )}
        </SelectedSession.Provider>
      </div>
      {session && (
        <SessionPanel
          session={session}
          finding={data.findings.find((finding) => finding.session === session.id) ?? null}
          waits={data.waits}
          advanced={advanced}
          onFilter={filters.add}
          onClose={close}
        />
      )}
    </div>
  );
}

/** Props for {@link LiveHeader}. */
interface LiveHeaderProps {
  /** The tab's data. */
  live: DatabaseLive;
  /** The latest error, shown beside data that may be old. */
  error: string | null;
  /** Goes back to now. */
  onNow: () => void;
}

/** How fresh the read is, or which moment is looked back at. */
function LiveHeader({ live, error, onNow }: LiveHeaderProps) {
  if (live.at) {
    return (
      <p className="live-banner" role="status">
        Looking back at {live.sampledAt ? formatClockTime(live.sampledAt) : formatClockTime(live.at)}
        {live.sampledAt === null && ": no read from around then."}
        <button type="button" className="link-button" onClick={onNow}>
          Back to now
        </button>
      </p>
    );
  }

  return (
    <p className="live-fresh" role="status">
      <i aria-hidden="true" />
      Live
      <small>
        {live.ageSeconds === null ? " · no read yet" : ` · newest read ${Math.round(live.ageSeconds)} s old`}
        {error && ` · could not refresh: ${error}`}
      </small>
    </p>
  );
}

/** Props for the two views of the tab. */
interface ViewProps {
  /** The tab's data. */
  live: DatabaseLive;
  /** The page's filters. */
  filters: LiveFilters;
  /** Opens a session's panel. */
  onSession: (id: number) => void;
}

/** The Simple view: Veyra's findings in words first, then who waits for whom, then what runs. */
function Simple({ live, filters, onSession, onAdvanced }: ViewProps & { onAdvanced: () => void }) {
  const [lead, ...rest] = live.findings;
  const byId = useMemo(() => new Map(live.sessions.map((session) => [session.id, session])), [live.sessions]);
  const running = live.sessions.filter((session) => session.status !== "sleeping" && meets(session, filters.filters));
  const selected = useContext(SelectedSession);
  const { tiles } = live;

  return (
    <>
      {lead && <Lead finding={lead} live={live} onSession={onSession} onAdvanced={onAdvanced} />}

      <section className="live-tiles four" aria-label="Now">
        <LiveTile
          label="Requests"
          value={formatRate(tiles.batchRequests)}
          unit="a second"
          line={tiles.batchRequestsLine}
        />
        <LiveTile
          label="Waiting on others"
          value={String(tiles.blocked)}
          unit={tiles.blocked === 1 ? "request" : "requests"}
          tone={tiles.blocked > 0 ? "bad" : undefined}
          below={tiles.blocked > 0 ? "see who waits for whom" : "nobody waits"}
        />
        <LiveTile
          label="Longest request"
          value={clock(tiles.longestRequestSeconds)}
          tone={tiles.longestRequestSeconds >= 300 ? "warn" : undefined}
        />
        <LiveTile
          label="Oldest open transaction"
          value={tiles.oldestTransactionSeconds === null ? "none" : clock(tiles.oldestTransactionSeconds)}
          tone={tiles.oldestTransactionSeconds !== null && tiles.oldestTransactionSeconds >= 300 ? "warn" : undefined}
          onOpen={
            tiles.oldestTransactionSession === null
              ? undefined
              : () => onSession(tiles.oldestTransactionSession as number)
          }
        />
      </section>

      <FilterBar filters={filters} />

      {live.blocking.length > 0 && (
        <section className="metric-panel" aria-labelledby="live-chains" id="live-blocking">
          <div className="metric-panel-head">
            <h2 id="live-chains">Who waits for whom</h2>
            <span>left holds, right waits</span>
          </div>
          {chainsMeeting(live.blocking, byId, filters.filters).map((tree) => (
            <Chain key={tree.session} tree={tree} byId={byId} onSession={onSession} />
          ))}
        </section>
      )}

      <section className="metric-panel" aria-labelledby="live-running">
        <div className="metric-panel-head">
          <h2 id="live-running">Running now</h2>
          <span>longest first</span>
        </div>
        {running.length === 0 ? (
          <p className="metrics-empty">
            {filters.filters.length > 0
              ? "No request that meets the filters is running."
              : "Nothing is running right now. Veyra reads the server every 10 seconds; a request shows here while it runs."}
          </p>
        ) : (
          <ul className="live-plain">
            {running.map((session) => (
              <li key={session.id} className={session.id === selected ? "selected" : undefined}>
                <button type="button" className="live-row-open" onClick={() => onSession(session.id)}>
                  <span>
                    <b>{session.name}</b>
                    <small>
                      {programName(session.program)} · {doingWords(session, live.waits)}
                    </small>
                  </span>
                  <span className="live-time">{clock(session.elapsedSeconds)}</span>
                </button>
                <span className="live-row-chips">
                  <FilterChip kind="database" value={session.database} onFilter={filters.add} />
                  <FilterChip kind="program" value={session.program} onFilter={filters.add} />
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {rest.length > 0 && (
        <section className="metric-panel" aria-labelledby="live-more">
          <div className="metric-panel-head">
            <h2 id="live-more">Also worth a look</h2>
          </div>
          <ul className="live-findings">
            {rest.map((finding) => (
              <FindingRow
                key={`${finding.kind}-${finding.session ?? ""}`}
                finding={finding}
                live={live}
                onSession={onSession}
              />
            ))}
          </ul>
        </section>
      )}
    </>
  );
}

/** Props for {@link Lead}. */
interface LeadProps {
  /** The worst finding. */
  finding: Finding;
  /** The tab's data. */
  live: DatabaseLive;
  /** Opens a session's panel. */
  onSession: (id: number) => void;
  /** Switches to the Advanced view. */
  onAdvanced: () => void;
}

/** The Simple view's opening sentence: the worst finding, why it matters, and what can be done. */
function Lead({ finding, live, onSession, onAdvanced }: LeadProps) {
  return (
    <section className={`live-lead ${finding.severity}`} aria-label="How the server is">
      <i aria-hidden="true" />
      <div>
        <h2>{finding.title}</h2>
        <p>{finding.detail}</p>
        <FindingActions actions={finding.actions} sampledAt={live.sampledAt} onSession={onSession} />
      </div>
      {finding.severity !== "ok" && (
        <button type="button" className="link-button" onClick={onAdvanced}>
          See it in Advanced ›
        </button>
      )}
    </section>
  );
}

/** Props for {@link Chain}. */
interface ChainProps {
  /** The chain. */
  tree: BlockingNode;
  /** The sessions by ID. */
  byId: Map<number, LiveSession>;
  /** Opens a session's panel. */
  onSession: (id: number) => void;
}

/** One blocking chain in the Simple view: who holds, who waits for it, who waits behind them. */
function Chain({ tree, byId, onSession }: ChainProps) {
  const levels: LiveSession[][] = [];
  let current = tree.children;

  while (current.length > 0) {
    levels.push(current.map((node) => byId.get(node.session)).filter((one): one is LiveSession => one !== undefined));
    current = current.flatMap((node) => node.children);
  }

  const head = byId.get(tree.session);

  return (
    <div className="live-chain">
      <button type="button" className="live-link-card head" onClick={() => onSession(tree.session)}>
        <b>{head ? programName(head.program) : "a connection that ended"}</b>
        <small>
          {head?.host ? `${head.host} · ` : ""}connection {tree.session}
        </small>
        <span>
          {head?.status === "sleeping"
            ? head.changedData
              ? "changed data, did not finish"
              : "opened a transaction, did not finish"
            : head
              ? `${head.name.toLowerCase()}, running`
              : "ended"}
        </span>
        <span className="live-time bad">
          {head
            ? clock(head.status === "sleeping" ? (head.transactionSeconds ?? head.idleSeconds) : head.elapsedSeconds)
            : ""}
        </span>
        <small>holding</small>
      </button>
      {levels.map((level, index) => {
        const programs = [...new Set(level.map((one) => programName(one.program)))];
        const longest = Math.max(...level.map((one) => one.waitSeconds));
        const first = level[0];

        return (
          <div className="live-chain-step" key={index}>
            <span className="live-arrow" aria-hidden="true">
              →
            </span>
            <button
              type="button"
              className="live-link-card"
              onClick={() => first && onSession(first.id)}
              disabled={!first}
            >
              <b>
                {programs.join(", ")}
                {level.length > 1 ? ` × ${level.length}` : ""}
              </b>
              <small>{[...new Set(level.map((one) => one.host).filter(Boolean))].join(", ")}</small>
              <span>{first ? `${first.name.toLowerCase()}` : ""}</span>
              <span className="live-time warn">{clock(longest)}</span>
              <small>{level.length > 1 ? "waiting, longest" : "waiting"}</small>
            </button>
          </div>
        );
      })}
    </div>
  );
}

/** The Advanced view: findings in SQL Server's terms, every number, the blocking tree, and every session. */
function Advanced({ live, filters, onSession }: ViewProps) {
  const byId = useMemo(() => new Map(live.sessions.map((session) => [session.id, session])), [live.sessions]);
  const sessions = live.sessions.filter((session) => meets(session, filters.filters));
  const selected = useContext(SelectedSession);
  const { tiles } = live;

  return (
    <>
      <section className="metric-panel" aria-labelledby="live-findings">
        <div className="metric-panel-head">
          <h2 id="live-findings">Findings</h2>
          <span>worst first</span>
        </div>
        <ul className="live-findings">
          {live.findings.map((finding) => (
            <FindingRow
              key={`${finding.kind}-${finding.session ?? ""}`}
              finding={finding}
              live={live}
              onSession={onSession}
              advanced
            />
          ))}
        </ul>
      </section>

      <section className="live-tiles" aria-label="Now">
        <LiveTile
          label="Batch requests"
          value={formatRate(tiles.batchRequests)}
          unit="/s"
          line={tiles.batchRequestsLine}
        />
        <LiveTile
          label="Blocked"
          value={String(tiles.blocked)}
          unit="sessions"
          line={tiles.blockedLine}
          tone={tiles.blocked > 0 ? "bad" : undefined}
          onOpen={tiles.blocked > 0 ? () => document.getElementById("live-tree")?.focus() : undefined}
        />
        <LiveTile
          label="Longest request"
          value={clock(tiles.longestRequestSeconds)}
          tone={tiles.longestRequestSeconds >= 300 ? "warn" : undefined}
        />
        <LiveTile
          label="Oldest open transaction"
          value={tiles.oldestTransactionSeconds === null ? "none" : clock(tiles.oldestTransactionSeconds)}
          tone={tiles.oldestTransactionSeconds !== null && tiles.oldestTransactionSeconds >= 300 ? "warn" : undefined}
          onOpen={
            tiles.oldestTransactionSession === null
              ? undefined
              : () => onSession(tiles.oldestTransactionSession as number)
          }
        />
        <LiveTile
          label="Grants waiting"
          value={String(tiles.grantsWaiting)}
          tone={tiles.grantsWaiting > 0 ? "warn" : undefined}
        />
        <LiveTile
          label="tempdb free"
          value={formatShare(tiles.tempdbFreeRatio)}
          below={tiles.tempdbSizeBytes > 0 ? `of ${formatAmount(tiles.tempdbSizeBytes)}` : undefined}
          tone={tiles.tempdbFreeRatio !== null && tiles.tempdbFreeRatio < 0.1 ? "warn" : undefined}
        />
      </section>

      <FilterBar filters={filters} />

      {live.blocking.length > 0 && (
        <section className="metric-panel" aria-labelledby="live-tree-head">
          <div className="metric-panel-head">
            <h2 id="live-tree-head">Blocking now</h2>
            <span>head blocker first</span>
          </div>
          <ul className="live-tree" id="live-tree" tabIndex={-1}>
            {chainsMeeting(live.blocking, byId, filters.filters).map((tree) => (
              <TreeNode
                key={tree.session}
                node={tree}
                byId={byId}
                waits={live.waits}
                filters={filters}
                onSession={onSession}
              />
            ))}
          </ul>
        </section>
      )}

      <section className="metric-panel" aria-labelledby="live-sessions">
        <div className="metric-panel-head">
          <h2 id="live-sessions">Sessions</h2>
          <span>
            {sessions.length === 1 ? "1 session" : `${sessions.length} sessions`} running, waiting, or holding a
            transaction
          </span>
        </div>
        {sessions.length === 0 ? (
          <p className="metrics-empty">No session{filters.filters.length > 0 ? " meets the filters" : " is busy"}.</p>
        ) : (
          <div className="live-table-wrap">
            <table className="route-table live-table">
              <thead>
                <tr>
                  <th scope="col">Session</th>
                  <th scope="col" className="number">
                    Time
                  </th>
                  <th scope="col">Database</th>
                  <th scope="col">State</th>
                  <th scope="col" className="number">
                    CPU
                  </th>
                  <th scope="col" className="number">
                    Reads
                  </th>
                  <th scope="col" className="number">
                    tempdb
                  </th>
                  <th scope="col">Program</th>
                  <th scope="col">Statement</th>
                </tr>
              </thead>
              <tbody>
                {sessions.map((session) => (
                  <tr key={session.id} className={session.id === selected ? "selected" : undefined}>
                    <th scope="row">
                      <button type="button" className="link-button mono" onClick={() => onSession(session.id)}>
                        {session.id}
                      </button>
                    </th>
                    <td data-label="Time" className="number">
                      {session.status === "sleeping"
                        ? session.transactionSeconds === null
                          ? "–"
                          : `tx ${clock(session.transactionSeconds)}`
                        : clock(session.elapsedSeconds)}
                    </td>
                    <td data-label="Database">
                      <FilterChip kind="database" value={session.database} onFilter={filters.add} />
                    </td>
                    <td data-label="State">
                      <State session={session} waits={live.waits} onFilter={filters.add} />
                    </td>
                    <td data-label="CPU" className="number">
                      {session.cpuSeconds.toFixed(1)} s
                    </td>
                    <td data-label="Reads" className="number">
                      {shortCount(session.reads)}
                    </td>
                    <td data-label="tempdb" className="number">
                      {session.tempdbBytes > 0 ? formatAmount(session.tempdbBytes) : "–"}
                    </td>
                    <td data-label="Program">
                      <FilterChip kind="program" value={session.program} onFilter={filters.add} />
                    </td>
                    <td data-label="Statement">
                      <button type="button" className="live-text" onClick={() => onSession(session.id)}>
                        {session.text}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

/** Props for {@link State}. */
interface StateProps {
  /** The session. */
  session: LiveSession;
  /** The wait types explained. */
  waits: Record<string, WaitTerm>;
  /** Filters the page. */
  onFilter: (kind: FilterKind, value: string) => void;
}

/** A session's state: its wait type that explains itself, or running, or sleeping. */
function State({ session, waits, onFilter }: StateProps) {
  const term = session.waitType ? waits[session.waitType] : undefined;

  if (term) {
    return <WaitPill term={term} onFilter={onFilter} />;
  }

  return <span className={`live-pill ${session.status === "sleeping" ? "bad" : "ok"}`}>{session.status}</span>;
}

/** Props for {@link TreeNode}. */
interface TreeNodeProps {
  /** The node. */
  node: BlockingNode;
  /** The sessions by ID. */
  byId: Map<number, LiveSession>;
  /** The wait types explained. */
  waits: Record<string, WaitTerm>;
  /** The page's filters. */
  filters: LiveFilters;
  /** Opens a session's panel. */
  onSession: (id: number) => void;
}

/** One session of the blocking tree, with the sessions waiting for it under it. */
function TreeNode({ node, byId, waits, filters, onSession }: TreeNodeProps) {
  const session = byId.get(node.session);
  const head = session?.blockedBy === null || session === undefined;
  const open = useContext(SelectedSession) === node.session;

  return (
    <li className={head ? "head" : undefined}>
      <div className={`live-node ${open ? "selected" : ""}`} aria-current={open ? "true" : undefined}>
        <button type="button" className="live-node-id" onClick={() => onSession(node.session)} disabled={!session}>
          <small>session</small>
          <b>{node.session}</b>
        </button>
        <div className="live-node-body">
          <span className="live-node-tags">
            {session ? (
              <State session={session} waits={waits} onFilter={filters.add} />
            ) : (
              <span className="live-pill">ended</span>
            )}
            {session && session.status === "sleeping" && session.transactionSeconds !== null && (
              <span className="live-pill bad">OPEN TRANSACTION</span>
            )}
            {session && <FilterChip kind="program" value={session.program} onFilter={filters.add} />}
            {session && <FilterChip kind="host" value={session.host} onFilter={filters.add} />}
            {session && <FilterChip kind="login" value={session.login} onFilter={filters.add} />}
          </span>
          {session && (
            <button type="button" className="live-text" onClick={() => onSession(session.id)}>
              {session.text}
            </button>
          )}
        </div>
        <span className="live-node-time">
          {session
            ? session.status === "sleeping"
              ? `tx ${clock(session.transactionSeconds ?? session.idleSeconds)}`
              : session.blockedBy === null
                ? clock(session.elapsedSeconds)
                : `waits ${clock(session.waitSeconds)}`
            : ""}
          {node.waiting > 0 && <small>{node.waiting} behind</small>}
        </span>
      </div>
      {node.children.length > 0 && (
        <ul>
          {node.children.map((child) => (
            <TreeNode
              key={child.session}
              node={child}
              byId={byId}
              waits={waits}
              filters={filters}
              onSession={onSession}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

/** Props for {@link FindingRow}. */
interface FindingRowProps {
  /** The finding. */
  finding: Finding;
  /** The tab's data. */
  live: DatabaseLive;
  /** Opens a session's panel. */
  onSession: (id: number) => void;
  /** Whether to show SQL Server's terms. */
  advanced?: boolean;
}

/** One finding in a list, with its actions. */
function FindingRow({ finding, live, onSession, advanced = false }: FindingRowProps) {
  return (
    <li className={`live-finding ${finding.severity}`}>
      <i aria-hidden="true" />
      <div>
        <b>{finding.title}</b>
        <p>{advanced ? finding.advanced : finding.detail}</p>
        <FindingActions actions={finding.actions} sampledAt={live.sampledAt} onSession={onSession} />
      </div>
    </li>
  );
}

/** Props for {@link FilterBar}. */
interface FilterBarProps {
  /** The page's filters. */
  filters: LiveFilters;
}

/** The filters on the page, each removable, kept in the address. Shows nothing without filters. */
function FilterBar({ filters }: FilterBarProps) {
  if (filters.filters.length === 0) {
    return null;
  }

  return (
    <div className="live-filters" role="group" aria-label="Filters">
      <span className="faint">Filtered by</span>
      {filters.filters.map((filter) => (
        <span key={filter.kind} className="live-filter">
          {filter.kind} <b>{filter.value}</b>
          <button
            type="button"
            aria-label={`Remove the ${filter.kind} filter`}
            onClick={() => filters.remove(filter.kind)}
          >
            ✕
          </button>
        </span>
      ))}
      <button type="button" className="link-button" onClick={filters.clear}>
        Clear
      </button>
    </div>
  );
}
