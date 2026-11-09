import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef, type KeyboardEvent, type UIEvent } from "react";

import type { LogEntry } from "../api/types";
import { formatTime } from "./format";
import { SeverityBadge } from "./SeverityBadge";

/** How close to the end of the list, in pixels, scrolling starts loading the next page. */
const loadAheadPixels = 600;

/** Props for {@link LogList}. */
interface LogListProps {
  /** The records loaded so far, newest first. */
  items: LogEntry[];
  /** The selected record, or null. */
  selectedId: number | null;
  /** Called when the person picks a record, by pointer or with the arrow keys. */
  onSelect: (entry: LogEntry) => void;
  /** Whether more records exist beyond those loaded. */
  hasMore: boolean;
  /** Loads the next page. */
  onLoadMore: () => void;
}

/**
 * The result list. Only the rows in view are rendered, so tens of thousands of loaded records stay fast; scrolling
 * near the end loads the next page. Rows are buttons: Tab reaches the list, the arrow keys move the selection.
 * On phones the same rows are laid out as cards by logs.css.
 */
export function LogList({ items, selectedId, onSelect, hasMore, onLoadMore }: LogListProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

  // The virtualizer returns new functions on every render by design; the React Compiler cannot memoise around it,
  // and nothing here relies on that.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => 39,
    overscan: 12,
    getItemKey: (index) => items[index]?.id ?? index,
  });

  function onScroll(event: UIEvent<HTMLDivElement>) {
    const element = event.currentTarget;

    if (hasMore && element.scrollTop + element.clientHeight >= element.scrollHeight - loadAheadPixels) {
      onLoadMore();
    }
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
      return;
    }

    event.preventDefault();
    const current = items.findIndex((entry) => entry.id === selectedId);
    const next = Math.min(items.length - 1, Math.max(0, current + (event.key === "ArrowDown" ? 1 : -1)));
    const entry = items[next];

    if (!entry) {
      return;
    }

    onSelect(entry);
    virtualizer.scrollToIndex(next);

    // The row may only exist after the virtualizer renders it.
    requestAnimationFrame(() =>
      scrollRef.current?.querySelector<HTMLButtonElement>(`[data-id="${entry.id}"]`)?.focus(),
    );
  }

  return (
    <div className="log-list" ref={scrollRef} onScroll={onScroll} onKeyDown={onKeyDown}>
      <div className="log-header" aria-hidden="true">
        <span>Time (UTC)</span>
        <span>Severity</span>
        <span>Service</span>
        <span>Message</span>
      </div>
      <div className="log-rows" role="list" aria-label="Log records" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((row) => {
          const entry = items[row.index];

          if (!entry) {
            return null;
          }

          return (
            <div
              key={row.key}
              role="listitem"
              className="log-row-slot"
              data-index={row.index}
              ref={virtualizer.measureElement}
              style={{ transform: `translateY(${row.start}px)` }}
            >
              <button
                type="button"
                className="log-row"
                data-id={entry.id}
                aria-current={entry.id === selectedId ? "true" : undefined}
                onClick={() => onSelect(entry)}
              >
                <span className="log-time mono">{formatTime(entry.timestamp)}</span>
                <SeverityBadge severity={entry.severity} />
                <span className="log-service">{entry.serviceName}</span>
                <span className="log-message">{entry.body}</span>
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}
