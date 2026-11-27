import { useEffect, useState } from "react";

import { ApiError } from "../api/client";
import type { ResourcePage } from "../api/types";
import { useRefreshEvery } from "../app/shellContext";

/** How often a list asks again outside a shell, as in a component test. */
const ownRefreshMs = 30_000;

/** A resource list as far as it is loaded. */
export interface ResourcePages<THeader, TItem> {
  /** The header of the latest answer, or null before the first. */
  header: THeader | null;
  /** Every item loaded so far, in the server's order. */
  items: TItem[];
  /** How many items the filters keep. */
  total: number;
  /** Whether there is a page after the loaded ones. */
  more: boolean;
  /** Whether a page is on its way. */
  loading: boolean;
  /** Why the last request failed, or null. */
  error: string | null;
  /** Loads the next page and adds it to the list. */
  loadMore(): void;
}

/**
 * One resource list, page by page (ADR 0022): the first page follows the filters, the window, and the shell's
 * refresh; Load more adds the next page the server points to. A change of filters starts the list over.
 *
 * @param key - What the list is: its filters, window, and environment, in one string.
 * @param fetch - Asks the server for a page, from a cursor or from the start.
 * @param choices - What the list is asked with that does not make it another list, such as how many rows fit on the
 *   screen (VEY-81): a change asks again at once but keeps the list on screen until the answer arrives.
 * @returns The list loaded so far and the way to load more.
 */
export function useResourcePages<THeader, TItem>(
  key: string,
  fetch: (cursor: string | null, signal: AbortSignal) => Promise<ResourcePage<THeader, TItem>>,
  choices = "",
): ResourcePages<THeader, TItem> {
  const every = useRefreshEvery(ownRefreshMs);
  const [state, setState] = useState({
    key,
    header: null as THeader | null,
    items: [] as TItem[],
    total: 0,
    next: null as string | null,
    loading: true,
    error: null as string | null,
    pages: 1,
  });

  // Other filters start from nothing, so the old list never shows under the new filters.
  if (state.key !== key) {
    setState({ key, header: null, items: [], total: 0, next: null, loading: true, error: null, pages: 1 });
  }

  const pages = state.key === key ? state.pages : 1;

  // The first pages again, on a change and on every refresh: as many pages as are loaded, so the list keeps its length.
  useEffect(() => {
    const controller = new AbortController();

    async function load() {
      try {
        let cursor: string | null = null;
        let header: THeader | null = null;
        let items: TItem[] = [];
        let total = 0;

        for (let page = 0; page < pages; page += 1) {
          const answer: ResourcePage<THeader, TItem> = await fetch(cursor, controller.signal);
          header = answer.header;
          items = [...items, ...answer.items];
          total = answer.total;
          cursor = answer.next;

          if (cursor === null) {
            break;
          }
        }

        setState((previous) => ({ ...previous, header, items, total, next: cursor, loading: false, error: null }));
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") {
          return;
        }

        const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
        setState((previous) => ({ ...previous, loading: false, error: message }));
      }
    }

    void load();
    const timer = every === null ? null : window.setInterval(() => void load(), every);

    return () => {
      controller.abort();

      if (timer !== null) {
        window.clearInterval(timer);
      }
    };
    // The key stands for everything fetch depends on; fetch itself is a new function on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, pages, every, choices]);

  return {
    header: state.header,
    items: state.items,
    total: state.total,
    more: state.next !== null,
    loading: state.loading,
    error: state.error,
    loadMore: () => setState((previous) => ({ ...previous, pages: previous.pages + 1, loading: true })),
  };
}
