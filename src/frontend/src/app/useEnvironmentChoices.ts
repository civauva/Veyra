import { useEffect, useState } from "react";

import { api } from "../api/client";
import type { EnvironmentChoices } from "../api/types";

/**
 * The environments a person can switch to, as the server lists them for a search (VEY-67). Every look's environment
 * switcher draws this list its own way (ADR 0021); none of them asks the server itself.
 *
 * @param open - Whether the list is open; the server is asked only while it is.
 * @param query - What the find box holds; the server keeps the environments that match.
 * @returns The latest answer, which stays until the next one arrives; null before the first.
 */
export function useEnvironmentChoices(open: boolean, query: string): EnvironmentChoices | null {
  const [choices, setChoices] = useState<EnvironmentChoices | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    const controller = new AbortController();

    api
      .homeEnvironments(query, controller.signal)
      .then(setChoices)
      .catch(() => undefined);

    return () => controller.abort();
  }, [open, query]);

  return choices;
}
