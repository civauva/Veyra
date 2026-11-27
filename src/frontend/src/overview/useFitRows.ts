import { type RefObject, useEffect, useRef, useState } from "react";

/** What {@link fitRows} needs to know about a list in its tile. */
export interface FitMeasure {
  /** The rows the list shows now. */
  shown: number;
  /** The height of one row in pixels. */
  rowHeight: number;
  /** The empty height below the list in its tile, in pixels. */
  spare: number;
  /** The rows the screen asked the server for so far. */
  asked: number;
  /** The fewest rows to ask for, what fits a laptop. */
  least: number;
  /** The most rows to ask for, what the server answers at most. */
  most: number;
}

/**
 * The rows to ask the server for: those shown and as many more as fit in the empty room below them. It never asks
 * for fewer than before, so a list with fewer rows than asked, or one that grew its tile, does not shrink back and
 * forth; a tile without room keeps what it asked.
 *
 * @param measure - The list, its rows, and the room below it.
 * @returns How many rows to ask for.
 */
export function fitRows({ shown, rowHeight, spare, asked, least, most }: FitMeasure): number {
  if (shown === 0 || rowHeight <= 0) {
    return Math.max(least, asked);
  }

  const fit = shown + Math.floor(Math.max(0, spare) / rowHeight);

  return Math.min(most, Math.max(least, asked, fit));
}

/** What {@link useFitRows} returns. */
export interface FitRows {
  /** Goes on the tile's body, whose first child is the list. */
  box: RefObject<HTMLDivElement | null>;
  /** How many rows to ask the server for. */
  rows: number;
}

/** How long the tile's size has to stay put before the screen asks for more rows, so a resize asks once. */
const settleMs = 250;

/**
 * As many rows as fit in a tile that its row of tiles makes taller than its list (VEY-81). The tile's body is
 * measured as it resizes and as its list grows: the empty room below the last row, over the height of a row, is how
 * many more rows the screen asks the server for. Rows are found by a selector, such as `tbody > tr`.
 *
 * @param rowSelector - Finds the list's rows inside the tile's body.
 * @param least - The fewest rows to ask for.
 * @param most - The most rows to ask for.
 * @returns The body's ref and the rows to ask for.
 */
export function useFitRows(rowSelector: string, least: number, most = 50): FitRows {
  const box = useRef<HTMLDivElement>(null);
  const [rows, setRows] = useState(least);

  useEffect(() => {
    const body = box.current;

    if (!body || typeof ResizeObserver === "undefined") {
      return;
    }

    let timer: number | undefined;

    const measure = () => {
      const list = body.firstElementChild;
      const items = body.querySelectorAll(rowSelector);
      const last = items[items.length - 1];

      if (!list || !last) {
        return;
      }

      const spare = body.clientHeight - list.getBoundingClientRect().height;
      const rowHeight = last.getBoundingClientRect().height;

      setRows((asked) => fitRows({ shown: items.length, rowHeight, spare, asked, least, most }));
    };

    const settle = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(measure, settleMs);
    };

    const observer = new ResizeObserver(settle);
    observer.observe(body);

    // The list changes height as rows arrive; it is watched too, and again when it is replaced.
    const watched = new MutationObserver(() => {
      if (body.firstElementChild) {
        observer.observe(body.firstElementChild);
      }

      settle();
    });
    watched.observe(body, { childList: true, subtree: true });

    if (body.firstElementChild) {
      observer.observe(body.firstElementChild);
    }

    return () => {
      window.clearTimeout(timer);
      observer.disconnect();
      watched.disconnect();
    };
  }, [rowSelector, least, most]);

  return { box, rows };
}
