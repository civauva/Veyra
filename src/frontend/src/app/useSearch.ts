import { useEffect, useState } from "react";

import { api, ApiError } from "../api/client";
import type { SearchResults } from "../api/types";
import { windowOf, type TimeChoice } from "./time";

/** How long typing must pause before the server is asked. */
const pauseMs = 200;

/** What a search box shows. */
export interface SearchState {
  /** The latest answer for the words now in the box, or null before it. */
  results: SearchResults | null;
  /** Whether the server is being asked. */
  searching: boolean;
  /** Why the last search failed, or null. */
  error: string | null;
}

/**
 * Searches every signal for the words in a look's search box (ADR 0022). The server matches, ranks, and counts; it is
 * asked once typing pauses, and a newer search cancels the older one. Empty words ask nothing.
 *
 * @param words - What the box holds.
 * @param environment - The chosen environment, or null for all.
 * @param time - The shared range; a range ending now is worked out when the server is asked.
 * @returns The answer and whether one is on its way.
 */
export function useSearch(words: string, environment: string | null, time: TimeChoice): SearchState {
  const query = words.trim();
  const [state, setState] = useState<SearchState & { query: string }>({
    query: "",
    results: null,
    searching: false,
    error: null,
  });

  // Other words start from nothing, so an answer for the old words never shows under the new ones.
  if (state.query !== query) {
    setState({ query, results: null, searching: query !== "", error: null });
  }

  useEffect(() => {
    if (!query) {
      return undefined;
    }

    const controller = new AbortController();
    const timer = globalThis.setTimeout(() => {
      api
        .search(query, environment, windowOf(time, Date.now()), controller.signal)
        .then((results) => setState({ query, results, searching: false, error: null }))
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === "AbortError") {
            return;
          }

          const message = error instanceof ApiError ? error.message : "Veyra could not be reached.";
          setState({ query, results: null, searching: false, error: message });
        });
    }, pauseMs);

    return () => {
      globalThis.clearTimeout(timer);
      controller.abort();
    };
    // The range's parts stand for the range: a new object naming the same range asks nothing again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, environment, time.preset, time.from, time.to]);

  return { results: state.results, searching: state.searching, error: state.error };
}
