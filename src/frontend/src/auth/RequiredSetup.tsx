import { useCallback, useRef } from "react";

import { api } from "../api/client";
import type { Session } from "../api/types";
import { SetupSteps } from "../twofactor/SetupSteps";

/** Props for {@link RequiredSetup}. */
interface RequiredSetupProps {
  /** The email the person signs in with, the account name in the app. */
  email: string;
  /** Called with the session once the second factor works and the codes are stored. */
  onSignedIn(session: Session): void;
  /** Called when the person backs out; the session never started. */
  onRestart(): void;
}

/**
 * Right after a correct password, when an Admin requires a second factor the account does not have yet: set it up
 * now, then the session starts (ADR 0011).
 */
export function RequiredSetup({ email, onSignedIn, onRestart }: RequiredSetupProps) {
  // The session arrives with the recovery codes; it starts only after the person confirmed storing them.
  const session = useRef<Session | null>(null);

  const start = useCallback(() => api.startRequiredSetup(), []);
  const confirm = useCallback(async (code: string) => {
    const confirmed = await api.confirmRequiredSetup(code);
    session.current = confirmed.session;

    return confirmed.recoveryCodes;
  }, []);

  return (
    <div className="auth-form">
      <p className="auth-policy">
        Your administrator asks you to sign in with a second factor. Set it up now; it takes a minute and you only do it
        once.
      </p>
      <SetupSteps
        account={email}
        start={start}
        confirm={confirm}
        onDone={() => session.current && onSignedIn(session.current)}
        onCancel={onRestart}
        cancelLabel="Sign out"
      />
    </div>
  );
}
