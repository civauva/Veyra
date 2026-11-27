import { useEffect, useState } from "react";

import { api, ApiError } from "../api/client";
import type { LogQueryTerm } from "../api/types";

/** Why a query did not parse, as the server said (ADR 0024). */
export interface QueryProblem {
  /** What the server said, ready to show. */
  message: string;
  /** Where in the text it stopped, counted from 0; null when it did not say. */
  position: number | null;
}

/** What {@link useLogQueryTerms} gives a screen. */
export interface LogQueryTerms {
  /** The terms of the query, where the server located them; empty for an empty query or one that did not parse. */
  terms: LogQueryTerm[];
  /** Why the query did not parse; null when it did, or while it is read. */
  problem: QueryProblem | null;
}

/** The answer for one query, remembered with the query it answers. */
interface Answer extends LogQueryTerms {
  /** The query the answer is for. */
  query: string;
}

/** The problem of a refused parse: the server's detail, and the position it names in the problem's extensions. */
function problemOf(failure: unknown): QueryProblem {
  if (failure instanceof ApiError && typeof failure.body === "object" && failure.body !== null) {
    const body = failure.body as { detail?: unknown; position?: unknown };

    return {
      message: typeof body.detail === "string" ? body.detail : failure.message,
      position: typeof body.position === "number" ? body.position : null,
    };
  }

  return { message: failure instanceof Error ? failure.message : String(failure), position: null };
}

/**
 * The terms of a log query as the server parses them, so the chips show and change exactly what the server will
 * filter by. The browser never interprets the query itself (ADR 0024).
 *
 * @param query - The query that runs, from the address.
 * @returns Its terms, or why it did not parse.
 */
export function useLogQueryTerms(query: string): LogQueryTerms {
  const text = query.trim();
  const [answer, setAnswer] = useState<Answer | null>(null);

  useEffect(() => {
    if (!text) {
      return undefined;
    }

    const controller = new AbortController();

    api.parseLogQuery(text, controller.signal).then(
      (parsed) => {
        if (!controller.signal.aborted) {
          setAnswer({ query: text, terms: parsed.terms, problem: null });
        }
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) {
          setAnswer({ query: text, terms: [], problem: problemOf(failure) });
        }
      },
    );

    return () => controller.abort();
  }, [text]);

  if (!text || answer?.query !== text) {
    return { terms: [], problem: null };
  }

  return { terms: answer.terms, problem: answer.problem };
}
