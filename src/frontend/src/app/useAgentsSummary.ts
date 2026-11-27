import { useEffect, useState } from "react";

import { api } from "../api/client";
import type { AgentsSummary } from "../api/types";

/** How often the summary is read again while a shell shows it. */
const everyMs = 60_000;

/**
 * The server's count of agents and how many are online, for a shell that shows it (ADR 0021). Only Admins may read
 * it, so for anyone else nothing is asked and the answer stays null.
 *
 * @param enabled - Whether to ask, which is whether the person is an Admin.
 * @returns The latest summary, or null before it arrives, when it fails, or when not asked.
 */
export function useAgentsSummary(enabled: boolean): AgentsSummary | null {
  const [summary, setSummary] = useState<AgentsSummary | null>(null);

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    const controller = new AbortController();

    function read() {
      api
        .agentsSummary(controller.signal)
        .then(setSummary)
        .catch(() => undefined);
    }

    read();
    const timer = window.setInterval(read, everyMs);

    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [enabled]);

  return enabled ? summary : null;
}
