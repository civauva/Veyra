import { useSearchParams } from "react-router";

import type { BlockingNode, LiveSession } from "../../api/types";

/** What the Live tab can be filtered by (ADR 0020): each is a chip on the page and a parameter in the address. */
export type FilterKind = "database" | "program" | "host" | "login" | "wait";

/** The filter kinds in the order the filter bar lists them. */
export const filterKinds: FilterKind[] = ["database", "program", "host", "login", "wait"];

/** One filter on the page. */
export interface LiveFilter {
  /** What it filters by. */
  kind: FilterKind;
  /** The value a session must have. */
  value: string;
}

/** What {@link useLiveFilters} returns. */
export interface LiveFilters {
  /** The filters on the page, in the filter bar's order. */
  filters: LiveFilter[];
  /** Adds a filter, or replaces the one of the same kind. */
  add(kind: FilterKind, value: string): void;
  /** Removes the filter of a kind. */
  remove(kind: FilterKind): void;
  /** Removes every filter. */
  clear(): void;
}

/**
 * The Live tab's filters, kept in the address so a filtered view is a link to share (ADR 0020).
 *
 * @returns The filters and the ways to change them.
 */
export function useLiveFilters(): LiveFilters {
  const [params, setParams] = useSearchParams();
  const filters = filterKinds
    .map((kind) => ({ kind, value: params.get(kind) ?? "" }))
    .filter((filter) => filter.value.length > 0);

  function change(apply: (next: URLSearchParams) => void) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        apply(next);

        return next;
      },
      { replace: true },
    );
  }

  return {
    filters,
    add: (kind, value) => change((next) => next.set(kind, value)),
    remove: (kind) => change((next) => next.delete(kind)),
    clear: () => change((next) => filterKinds.forEach((kind) => next.delete(kind))),
  };
}

/**
 * Whether a session meets every filter.
 *
 * @param session - The session.
 * @param filters - The filters.
 * @returns True when it meets all of them.
 */
export function meets(session: LiveSession, filters: LiveFilter[]): boolean {
  return filters.every((filter) => {
    switch (filter.kind) {
      case "database":
        return session.database === filter.value;
      case "program":
        return session.program === filter.value;
      case "host":
        return session.host === filter.value;
      case "login":
        return session.login === filter.value;
      case "wait":
        return session.waitType === filter.value;
    }
  });
}

/**
 * Keeps the blocking chains with at least one session that meets the filters.
 *
 * @param trees - The chains.
 * @param byId - The sessions by ID.
 * @param filters - The filters.
 * @returns The chains to show.
 */
export function chainsMeeting(
  trees: BlockingNode[],
  byId: Map<number, LiveSession>,
  filters: LiveFilter[],
): BlockingNode[] {
  if (filters.length === 0) {
    return trees;
  }

  function any(node: BlockingNode): boolean {
    const session = byId.get(node.session);

    return (session !== undefined && meets(session, filters)) || node.children.some(any);
  }

  return trees.filter(any);
}
