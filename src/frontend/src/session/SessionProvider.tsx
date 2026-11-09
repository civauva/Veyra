import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";

import { api } from "../api/client";
import { SessionContext, type SessionState, type SessionValue } from "./SessionContext";

/** Props for {@link SessionProvider}. */
interface SessionProviderProps {
  /** The app, which reads the session through {@link useSession}. */
  children: ReactNode;
}

/**
 * Finds out on start whether someone is signed in, or whether Veyra still needs its first administrator, and keeps
 * that state for every screen.
 */
export function SessionProvider({ children }: SessionProviderProps) {
  const [state, setState] = useState<SessionState>({ status: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const session = await api.currentSession();

        if (session) {
          return { status: "signedIn", session } as const;
        }

        const { required } = await api.setupStatus();

        return required ? ({ status: "setup" } as const) : ({ status: "signedOut" } as const);
      } catch (error) {
        return { status: "error", message: error instanceof Error ? error.message : String(error) } as const;
      }
    }

    void load().then((next) => {
      if (!cancelled) {
        setState(next);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const signIn = useCallback(async (email: string, password: string) => {
    const session = await api.signIn(email, password);

    setState({ status: "signedIn", session });
  }, []);

  const signOut = useCallback(async () => {
    await api.signOut();

    setState({ status: "signedOut" });
  }, []);

  const setupCompleted = useCallback(() => {
    setState({ status: "signedOut", notice: "Administrator created. Sign in to continue." });
  }, []);

  const value = useMemo<SessionValue>(
    () => ({ state, signIn, signOut, setupCompleted }),
    [state, signIn, signOut, setupCompleted],
  );

  return <SessionContext value={value}>{children}</SessionContext>;
}
