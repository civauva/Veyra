import { useCallback, useEffect, useState } from "react";

import { api } from "../api/client";
import type { IncidentContext, IncidentDetails, IncidentItem, IncidentState } from "../api/types";
import { useRefreshEvery } from "../app/shellContext";

/** How often incidents are read again outside a shell. */
const ownRefresh = 30_000;

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** What {@link useIncidents} gives a screen. */
export interface IncidentList {
  /** The incidents, open first, then newest; null before the first answer. */
  items: IncidentItem[] | null;
  /** Whether more exist beyond those loaded. */
  hasMore: boolean;
  /** Why the latest read failed, or null. */
  error: string | null;
  /** Loads the next page. */
  loadMore(): void;
}

/**
 * The incidents of a state, read again on the shell's refresh (ADR 0022, ADR 0026). The server orders and filters them.
 *
 * @param state - Which incidents.
 * @param days - How far back resolved ones go.
 * @param environment - The environment of the top bar, or null for all.
 * @returns The incidents and a way to load older ones.
 */
export function useIncidents(state: IncidentState | "all", days: number, environment: string | null): IncidentList {
  const refreshMs = useRefreshEvery(ownRefresh);
  const key = `${state}#${days}#${environment ?? ""}`;
  const [answer, setAnswer] = useState<{ key: string; items: IncidentItem[]; next: string | null } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    api.incidents(state, days, environment, null, controller.signal).then(
      (page) => {
        if (!controller.signal.aborted) {
          setAnswer({ key, items: page.items, next: page.next });
          setError(null);
        }
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) {
          setError(messageOf(failure));
        }
      },
    );

    return () => controller.abort();
  }, [key, state, days, environment, tick]);

  useEffect(() => {
    if (refreshMs === null) {
      return undefined;
    }

    const timer = window.setInterval(() => setTick((count) => count + 1), refreshMs);

    return () => window.clearInterval(timer);
  }, [refreshMs]);

  const current = answer?.key === key ? answer : null;

  const loadMore = useCallback(() => {
    if (!current?.next) {
      return;
    }

    api.incidents(state, days, environment, current.next).then(
      (page) =>
        setAnswer((before) =>
          before?.key === key ? { key, items: [...before.items, ...page.items], next: page.next } : before,
        ),
      (failure: unknown) => setError(messageOf(failure)),
    );
  }, [current, key, state, days, environment]);

  return { items: current?.items ?? null, hasMore: Boolean(current?.next), error, loadMore };
}

/** What {@link useIncident} gives a screen. */
export interface IncidentView {
  /** The incident, its alerts, figures, and timeline; null before it arrives. */
  details: IncidentDetails | null;
  /** What surrounds it; null before it arrives or when it could not be read. */
  context: IncidentContext | null;
  /** Whether the incident does not exist, or was deleted with its alerts. */
  notFound: boolean;
  /** Why the latest read or change failed, or null. */
  error: string | null;
  /** Whether an acknowledgement is being saved. */
  saving: boolean;
  /** Acknowledges it, or takes the acknowledgement back. */
  setAcknowledged(acknowledged: boolean): void;
}

/**
 * One incident and what surrounds it (ADR 0026), read again on the shell's refresh while it is not resolved.
 *
 * @param id - The incident's ID.
 * @returns The incident, its context, and the acknowledgement.
 */
export function useIncident(id: string): IncidentView {
  const refreshMs = useRefreshEvery(ownRefresh);
  const [details, setDetails] = useState<{ id: string; value: IncidentDetails } | null>(null);
  const [context, setContext] = useState<{ id: string; value: IncidentContext } | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [tick, setTick] = useState(0);
  const resolved = details?.id === id && details.value.incident.state === "resolved";

  useEffect(() => {
    const controller = new AbortController();

    api.incident(id, controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) {
          setDetails({ id, value });
          setNotFound(false);
          setError(null);
        }
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) {
          const missing =
            typeof failure === "object" && failure !== null && "status" in failure && failure.status === 404;
          setNotFound(missing);
          setError(missing ? null : messageOf(failure));
        }
      },
    );
    api.incidentContext(id, controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) {
          setContext({ id, value });
        }
      },
      () => undefined,
    );

    return () => controller.abort();
  }, [id, tick]);

  useEffect(() => {
    if (refreshMs === null || resolved) {
      return undefined;
    }

    const timer = window.setInterval(() => setTick((count) => count + 1), refreshMs);

    return () => window.clearInterval(timer);
  }, [refreshMs, resolved]);

  const setAcknowledged = useCallback(
    (acknowledged: boolean) => {
      setSaving(true);
      (acknowledged ? api.acknowledgeIncident(id) : api.unacknowledgeIncident(id))
        .then(
          () => setTick((count) => count + 1),
          (failure: unknown) => setError(messageOf(failure)),
        )
        .finally(() => setSaving(false));
    },
    [id],
  );

  return {
    details: details?.id === id ? details.value : null,
    context: context?.id === id ? context.value : null,
    notFound,
    error,
    saving,
    setAcknowledged,
  };
}
