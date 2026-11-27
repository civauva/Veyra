import { useSearchParams } from "react-router";

import type { LiveFilterValues } from "../../api/types";

/**
 * What the Live tab can be filtered by (ADR 0020): each is a chip on the page, a parameter in the address, and a
 * parameter of the request, so the server filters (VEY-66).
 */
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
 * The filters as the request sends them: the server lists only the sessions with every value given, and the chains
 * they are in (VEY-66).
 *
 * @param filters - The filters on the page.
 * @returns The values by kind.
 */
export function filterValues(filters: LiveFilter[]): LiveFilterValues {
  return Object.fromEntries(filters.map((filter) => [filter.kind, filter.value]));
}
