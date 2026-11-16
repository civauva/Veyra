import { useEffect, useRef, useState } from "react";

import type { Finding, LiveSession, WaitTerm } from "../../api/types";
import { formatAmount, formatSeconds } from "../metricFormat";
import type { FilterKind } from "./liveFilters";
import { clock, copyText, doingWords, programName, shortCount } from "./liveFormat";
import { FilterChip, WaitPill } from "./liveParts";

/** Props for {@link SessionPanel}. */
export interface SessionPanelProps {
  /** The session. */
  session: LiveSession;
  /** The finding about it, when there is one, which leads the panel. */
  finding: Finding | null;
  /** The wait types explained. */
  waits: Record<string, WaitTerm>;
  /** Whether the Advanced view is on. */
  advanced: boolean;
  /** Filters the page. */
  onFilter: (kind: FilterKind, value: string) => void;
  /** Closes the panel. */
  onClose: () => void;
}

/**
 * One session of the Live tab in full (ADR 0020): what it does, who it is, its transaction, its statement, and the
 * statement to end it, which a DBA runs. Veyra itself only reads. Escape closes it.
 */
export function SessionPanel({ session, finding, waits, advanced, onFilter, onClose }: SessionPanelProps) {
  const heading = useRef<HTMLHeadingElement>(null);
  const [copied, setCopied] = useState<"kill" | "text" | null>(null);
  const term = session.waitType ? waits[session.waitType] : undefined;
  const head = session.blocking > 0 && session.blockedBy === null;

  useEffect(() => {
    heading.current?.focus();
  }, [session.id]);

  useEffect(() => {
    function keyed(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    document.addEventListener("keydown", keyed);

    return () => document.removeEventListener("keydown", keyed);
  }, [onClose]);

  async function copy(what: "kill" | "text", text: string) {
    setCopied((await copyText(text)) ? what : null);
  }

  return (
    <aside className="live-panel" role="dialog" aria-modal="false" aria-labelledby="live-panel-title">
      <div className="live-panel-head">
        <h2 id="live-panel-title" ref={heading} tabIndex={-1}>
          {advanced ? `Session ${session.id}` : `Connection ${session.id}`}
        </h2>
        {head && <span className="live-pill bad">{advanced ? "HEAD BLOCKER" : "others wait for it"}</span>}
        <button type="button" className="button small quiet" onClick={onClose} aria-label="Close the session">
          ✕
        </button>
      </div>

      {finding ? (
        <p className={`live-panel-say ${finding.severity}`}>
          <b>{advanced ? finding.advanced : finding.title}</b> {advanced ? null : finding.detail}
        </p>
      ) : (
        <p className="live-panel-say ok">
          {session.name}: {doingWords(session, waits)}
          {session.status === "sleeping" ? "." : `, for ${clock(session.elapsedSeconds)}.`}
        </p>
      )}

      <dl className="live-facts">
        <dt>Program</dt>
        <dd>
          <FilterChip kind="program" value={session.program} onFilter={onFilter} />
          {session.program.trim().length === 0 && programName(session.program)}
        </dd>
        <dt>Host · login</dt>
        <dd>
          <FilterChip kind="host" value={session.host} onFilter={onFilter} />{" "}
          <FilterChip kind="login" value={session.login} onFilter={onFilter} />
        </dd>
        <dt>Database</dt>
        <dd>
          <FilterChip kind="database" value={session.database} onFilter={onFilter} />
        </dd>
        <dt>Doing</dt>
        <dd>
          {session.status === "sleeping"
            ? doingWords(session, waits)
            : `${session.command || "a request"} for ${clock(session.elapsedSeconds)}`}
          {term && (
            <>
              {" · "}
              <WaitPill term={term} simple={!advanced} onFilter={onFilter} />
            </>
          )}
        </dd>
        {session.transactionSeconds !== null && (
          <>
            <dt>Transaction</dt>
            <dd>
              open for {clock(session.transactionSeconds)}
              {session.changedData ? ", changed data" : ", changed nothing yet"}
              {advanced && `, ${session.isolation}`}
            </dd>
          </>
        )}
        {session.percentComplete !== null && (
          <>
            <dt>Progress</dt>
            <dd>
              {session.percentComplete.toFixed(0)}% done, about {formatSeconds(session.remainingSeconds)} to go
            </dd>
          </>
        )}
        {advanced && (
          <>
            <dt>Work</dt>
            <dd>
              CPU {formatSeconds(session.cpuSeconds)} · {shortCount(session.reads)} reads · {shortCount(session.writes)}{" "}
              writes
            </dd>
            <dt>Memory</dt>
            <dd>
              tempdb {formatAmount(session.tempdbBytes)} · grant {formatAmount(session.grantBytes)}
              {session.grantWaiting && ` (waiting for ${formatAmount(session.grantRequestedBytes)})`}
            </dd>
            {session.waitResource && (
              <>
                <dt>Resource</dt>
                <dd className="mono">{session.waitResource}</dd>
              </>
            )}
            <dt>Connected</dt>
            <dd>{clock(session.connectedSeconds)} ago</dd>
            {session.queryHash && (
              <>
                <dt>Query hash</dt>
                <dd className="mono">{session.queryHash}</dd>
              </>
            )}
          </>
        )}
      </dl>

      <pre className="live-sql">{session.text || "Nothing sent yet."}</pre>

      <div className="live-actions">
        <button type="button" className="button small" onClick={() => void copy("text", session.text)}>
          {copied === "text" ? "Copied" : "Copy the statement"}
        </button>
        <button type="button" className="button small" onClick={() => void copy("kill", `KILL ${session.id};`)}>
          {copied === "kill" ? "Copied" : `Copy KILL ${session.id}`}
        </button>
      </div>
      <p className="faint live-panel-note">
        Veyra only reads. Ending a connection is a DBA&apos;s decision: it rolls back its open transaction.
      </p>
    </aside>
  );
}
