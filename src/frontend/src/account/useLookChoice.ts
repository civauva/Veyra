import { useState } from "react";

import { ApiError } from "../api/client";
import { lookIdOf, lookIds, lookNames, type LookId } from "../api/looks";
import { useSession } from "../session/useSession";

/** What each look is like, in a sentence. */
export const lookWords: Record<LookId, string> = {
  console: "Dense panels and tables: everything at a glance, made for daily work.",
  forecast: "The environment read like a weather report: one condition, the next hours, the outlook.",
};

/** One look a person can pick. */
export interface LookOption {
  /** Its stable name. */
  id: LookId;
  /** Its name on screen. */
  name: string;
  /** What it is like, in a sentence. */
  words: string;
}

/** Every look this app has, in the order they are offered; only looks that exist are offered (ADR 0029). */
export const lookOptions: LookOption[] = lookIds.map((id) => ({ id, name: lookNames[id], words: lookWords[id] }));

/** What {@link useLookChoice} returns. */
export interface LookChoiceState {
  /** The look the person sees now. */
  shown: LookId;
  /** The instance's default look, which they see until they pick one. */
  fallback: LookId;
  /** Whether people may pick their own look. */
  mayChoose: boolean;
  /** Whether the person picked a look of their own. */
  own: boolean;
  /** Whether a choice is being kept. */
  busy: boolean;
  /** Why the last choice could not be kept, or null. */
  error: string | null;
  /**
   * Keeps a look on the person's account, or goes back to the default with null.
   *
   * @param look - The look, or null.
   */
  choose(look: LookId | null): Promise<void>;
}

/**
 * The look of the whole app for the signed-in person (VEY-71, ADR 0021), kept on their account so it follows them to
 * every device. Every look's Account page and Settings draw it their own way.
 *
 * @returns The choice and how to change it; null when nobody is signed in.
 */
export function useLookChoice(): LookChoiceState | null {
  const { state, setLook } = useSession();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (state.status !== "signedIn") {
    return null;
  }

  const choice = state.session.look;

  async function choose(look: LookId | null) {
    setBusy(true);
    setError(null);

    try {
      await setLook(look);
    } catch (problem) {
      setError(problem instanceof ApiError ? problem.message : "The look could not be kept. Try again.");
    } finally {
      setBusy(false);
    }
  }

  return {
    shown: lookIdOf(choice),
    fallback: lookIdOf({ ...choice, chosen: null }),
    mayChoose: choice.mayChoose,
    own: choice.chosen !== null,
    busy,
    error,
    choose,
  };
}
