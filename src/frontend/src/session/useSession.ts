import { useContext } from "react";

import { SessionContext, type SessionValue } from "./SessionContext";

/**
 * Reads the session: who is signed in, and the sign-in and sign-out actions.
 *
 * @returns The session value.
 * @throws When used outside {@link SessionProvider}, which is a programming error.
 */
export function useSession(): SessionValue {
  const value = useContext(SessionContext);

  if (!value) {
    throw new Error("useSession must be used inside <SessionProvider>.");
  }

  return value;
}
