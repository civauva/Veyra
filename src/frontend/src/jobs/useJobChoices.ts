import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router";

import type { JobSource, JobStatusFilter } from "../api/types";
import type { JobChoices } from "./useJobs";

/** How long typing must pause before the search goes to the server, in milliseconds. */
export const searchPause = 300;

/** The status filters, as the server takes them, in the order a screen offers them. */
export const statusChoices: [JobStatusFilter, string][] = [
  ["all", "All"],
  ["failing", "Failing"],
  ["running", "Running"],
];

/** The source filters, as the server takes them; null is both. */
export const sourceChoices: [JobSource | null, string][] = [
  [null, "All"],
  ["trace", "Traces"],
  ["sqlagent", "SQL Agent"],
];

/** What {@link useJobChoices} gives a screen. */
export interface JobChoiceControls {
  /** The choices to ask the server with, as the address holds them. */
  choices: JobChoices;
  /** The search box's text, which reaches the address once typing pauses. */
  text: string;
  /**
   * Changes the search box's text.
   *
   * @param text - The new text.
   */
  setText(text: string): void;
  /**
   * Chooses which jobs.
   *
   * @param status - Failing, running, or all.
   */
  setStatus(status: JobStatusFilter): void;
  /**
   * Chooses one source, or both.
   *
   * @param source - The source, or null for both.
   */
  setSource(source: JobSource | null): void;
}

/**
 * The jobs list's search, status, and source, kept in the address as `q`, `status`, and `source`, so a link or a
 * reload shows the same list. Each is a request parameter the server applies (ADR 0027); the search reaches the
 * address, and so the server, once typing pauses.
 *
 * @returns The choices and the ways to change them.
 */
export function useJobChoices(): JobChoiceControls {
  const [params, setParams] = useSearchParams();
  const q = params.get("q") ?? "";
  const status = parseStatus(params.get("status"));
  const source = parseSource(params.get("source"));
  const [text, setText] = useState(q);
  const [seen, setSeen] = useState(q);
  // The address as it is now. The search waits for typing to pause, and meanwhile a status, a source, or the range may
  // change; building on the address of when the typing started would undo them.
  const latest = useRef(params);

  useEffect(() => {
    latest.current = params;
  }, [params]);

  // The address changed from elsewhere, as going back: the box follows it.
  if (seen !== q) {
    setSeen(q);

    if (text.trim() !== q) {
      setText(q);
    }
  }

  useEffect(() => {
    if (text.trim() === q) {
      return undefined;
    }

    const timer = window.setTimeout(() => choose("q", text.trim()), searchPause);

    return () => window.clearTimeout(timer);
    // Only new text starts the pause; choose builds on the latest address itself.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text, q]);

  function choose(name: string, value: string | null) {
    const next = new URLSearchParams(latest.current);

    if (value === null || value === "" || (name === "status" && value === "all")) {
      next.delete(name);
    } else {
      next.set(name, value);
    }

    latest.current = next;
    setParams(next, { replace: true });
  }

  return {
    choices: { q, status, source },
    text,
    setText,
    setStatus: (value) => choose("status", value),
    setSource: (value) => choose("source", value),
  };
}

/** The status an address names, or all. */
function parseStatus(value: string | null): JobStatusFilter {
  return value === "failing" || value === "running" ? value : "all";
}

/** The source an address names, or null for both. */
function parseSource(value: string | null): JobSource | null {
  return value === "trace" || value === "sqlagent" ? value : null;
}
