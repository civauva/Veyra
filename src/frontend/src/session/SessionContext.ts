import { createContext } from "react";

import type { Session } from "../api/types";

/** Where the person stands when the app starts or after a sign-in, sign-out, or setup. */
export type SessionState =
  | { status: "loading" }
  | { status: "setup" }
  | { status: "signedOut"; notice?: string }
  | { status: "signedIn"; session: Session }
  | { status: "error"; message: string };

/** What the session context offers to screens. */
export interface SessionValue {
  /** The current state. */
  state: SessionState;
  /** Signs in; throws an ApiError the form can show. */
  signIn(email: string, password: string): Promise<void>;
  /** Signs out and returns to the sign-in screen. */
  signOut(): Promise<void>;
  /** Moves from first-run setup to sign-in, with a confirmation for the person. */
  setupCompleted(): void;
}

/** The session context. Use {@link useSession} to read it. */
export const SessionContext = createContext<SessionValue | null>(null);
