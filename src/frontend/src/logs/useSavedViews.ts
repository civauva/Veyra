import { useCallback, useEffect, useState } from "react";

import { api } from "../api/client";
import type { SavedView, SavedViewInput } from "../api/types";

/** What {@link useSavedViews} gives a screen. */
export interface SavedViews {
  /** The person's own views and those others shared; null before they arrive. */
  views: SavedView[] | null;
  /** Why the last read or change failed, or null. */
  error: string | null;
  /**
   * Saves the query as a new view.
   *
   * @param view - The name, the query, and whether it is shared.
   * @returns The saved view, or null when the server refused it.
   */
  save(view: Omit<SavedViewInput, "screen">): Promise<SavedView | null>;
  /**
   * Deletes a view of one's own.
   *
   * @param id - The view's ID.
   */
  remove(id: string): Promise<void>;
}

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/**
 * The saved views of a screen (ADR 0024): read when the screen opens, and again after each change, so the list is
 * always the server's.
 *
 * @param screen - The screen the views belong to, such as `logs`.
 * @returns The views, and functions that save and delete them.
 */
export function useSavedViews(screen: string): SavedViews {
  const [views, setViews] = useState<SavedView[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reads, setReads] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    api.savedViews(screen, controller.signal).then(
      (found) => {
        if (!controller.signal.aborted) {
          setViews(found);
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
  }, [screen, reads]);

  const save = useCallback(
    async (view: Omit<SavedViewInput, "screen">) => {
      try {
        const saved = await api.createView({ ...view, screen });
        setReads((count) => count + 1);

        return saved;
      } catch (failure) {
        setError(messageOf(failure));

        return null;
      }
    },
    [screen],
  );

  const remove = useCallback(async (id: string) => {
    try {
      await api.deleteView(id);
      setReads((count) => count + 1);
    } catch (failure) {
      setError(messageOf(failure));
    }
  }, []);

  return { views, error, save, remove };
}
