import { createContext } from "react";

import type { Session, SignInNext } from "../api/types";

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
  /**
   * The password step of signing in; throws an ApiError the form can show. Resolves to `null` when the session
   * started, or to the step that comes next (ADR 0011).
   */
  signIn(email: string, password: string): Promise<SignInNext | null>;
  /** Starts the session that a later sign-in step, such as the code, returned. */
  completeSignIn(session: Session): void;
  /** Signs out and returns to the sign-in screen. */
  signOut(): Promise<void>;
  /** Moves from first-run setup to sign-in, with a confirmation for the person. */
  setupCompleted(): void;
}

/** The session context. Use {@link useSession} to read it. */
export const SessionContext = createContext<SessionValue | null>(null);
