import { useState, type ReactNode } from "react";
import { Link } from "react-router";

import type { ChartLine, FindingAction, WaitTerm } from "../../api/types";
import { usePopover } from "../../app/usePopover";
import { Sparkline } from "../Sparkline";
import type { FilterKind } from "./liveFilters";
import { copyText } from "./liveFormat";

/** Props for {@link FilterChip}. */
export interface FilterChipProps {
  /** What the chip filters by. */
  kind: FilterKind;
  /** Its value, which it shows. */
  value: string;
  /** Adds the filter. */
  onFilter: (kind: FilterKind, value: string) => void;
}

/**
 * A database, program, host, or login as a chip that filters the whole page to it (ADR 0020). Empty values show
 * nothing, since there is nothing to filter by.
 */
export function FilterChip({ kind, value, onFilter }: FilterChipProps) {
  if (value.trim().length === 0) {
    return null;
  }

  return (
    <button
      type="button"
      className="live-chip"
      aria-label={`Filter by ${kind} ${value}`}
      title={`Filter by ${kind} ${value}`}
      onClick={() => onFilter(kind, value)}
    >
      {value}
    </button>
  );
}

/** Props for {@link WaitPill}. */
export interface WaitPillProps {
  /** The wait type explained. */
  term: WaitTerm;
  /** Whether to show the Simple view's words instead of the type's name. */
  simple?: boolean;
  /** Filters the page by the wait type. */
  onFilter: (kind: FilterKind, value: string) => void;
}

/**
 * A wait type that explains itself (ADR 0020): its name in the Advanced view, its words in the Simple view, and a
 * popup with what it means, what usually causes it, and a filter.
 */
export function WaitPill({ term, simple = false, onFilter }: WaitPillProps) {
  const { open, toggle, close, rootRef, buttonRef } = usePopover();

  return (
    <span className="live-explain" ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        className={`live-pill wait ${simple ? "words" : ""}`}
        aria-expanded={open}
        aria-label={`${term.type}: ${term.simple}. Explain`}
        onClick={toggle}
      >
        {simple ? term.simple : term.type}
      </button>
      {open && (
        <span className="live-popup" role="dialog" aria-label={`${term.type} explained`}>
          <b>
            {term.type} · {term.simple}
          </b>
          <span>{term.meaning}</span>
          <span className="faint">Usually: {term.causes}</span>
          <button
            type="button"
            className="link-button"
            onClick={() => {
              onFilter("wait", term.type);
              close(true);
            }}
          >
            Filter the page by {term.type}
          </button>
        </span>
      )}
    </span>
  );
}

/** Props for {@link FindingActions}. */
export interface FindingActionsProps {
  /** What the finding offers. */
  actions: FindingAction[];
  /** When the read was made, so a program's logs open around it. */
  sampledAt: string | null;
  /** Opens a session's panel. */
  onSession: (id: number) => void;
}

/** A finding's actions: open the session, look at a program's logs, copy a statement for a DBA to run. */
export function FindingActions({ actions, sampledAt, onSession }: FindingActionsProps) {
  const [copied, setCopied] = useState<string | null>(null);

  if (actions.length === 0) {
    return null;
  }

  return (
    <div className="live-actions">
      {actions.map((action) => {
        switch (action.kind) {
          case "session":
            return (
              <button
                key={action.kind}
                type="button"
                className="button small"
                onClick={() => onSession(Number(action.value))}
              >
                {action.label}
              </button>
            );
          case "logs": {
            const params = new URLSearchParams({ service: action.value });

            if (sampledAt) {
              params.set("around", sampledAt);
            }

            return (
              <Link key={action.kind} className="live-link" to={`/logs?${params.toString()}`}>
                {action.label} ›
              </Link>
            );
          }
          case "copy":
            return (
              <span key={action.kind} className="live-copy">
                <button
                  type="button"
                  className="button small"
                  onClick={() => void copyText(action.value).then((done) => setCopied(done ? action.value : null))}
                >
                  {copied === action.value ? "Copied" : action.label}
                </button>
                {action.hint && <small>{action.hint}</small>}
              </span>
            );
        }
      })}
    </div>
  );
}

/** Props for {@link LiveTile}. */
export interface LiveTileProps {
  /** What the number is. */
  label: string;
  /** The number, formatted. */
  value: string;
  /** The unit after it. */
  unit?: string;
  /** A line under it, words or a sparkline. */
  below?: ReactNode;
  /** Its line over the last 15 minutes. */
  line?: ChartLine;
  /** `warn` or `bad` when it needs a look. */
  tone?: "warn" | "bad";
  /** Opens what it counts; without it, the tile is not a button. */
  onOpen?: () => void;
}

/** One number at the top of the Live tab; a button when it opens what it counts (ADR 0020). */
export function LiveTile({ label, value, unit, below, line, tone, onOpen }: LiveTileProps) {
  const body = (
    <>
      <span className="live-tile-label">{label}</span>
      <b>
        {value}
        {unit && <small> {unit}</small>}
      </b>
      {below && <span className="live-tile-below">{below}</span>}
      {line && <Sparkline line={line} color={tone ? "var(--warning-fg)" : "var(--accent)"} />}
    </>
  );
  const className = `live-tile${tone ? ` ${tone}` : ""}`;

  return onOpen ? (
    <button type="button" className={`${className} opens`} onClick={onOpen} aria-label={`${label}: ${value}. Open`}>
      {body}
      <span className="live-tile-go" aria-hidden="true">
        ›
      </span>
    </button>
  ) : (
    <div className={className}>{body}</div>
  );
}
