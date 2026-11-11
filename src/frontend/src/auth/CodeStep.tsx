import { useState, type FormEvent } from "react";

import { api } from "../api/client";
import type { Session } from "../api/types";
import { CodeField } from "../twofactor/CodeField";
import "../twofactor/twofactor.css";

/** Props for {@link CodeStep}. */
interface CodeStepProps {
  /** The email the person signs in with, shown so they know which account the code is for. */
  email: string;
  /** Called with the session once the code was right. */
  onSignedIn(session: Session): void;
  /** Called when the person wants to start over with another account. */
  onRestart(): void;
}

/**
 * The second step of signing in (ADR 0011): six digits from the authenticator app, or one of the recovery codes for
 * a lost phone.
 */
export function CodeStep({ email, onSignedIn, onRestart }: CodeStepProps) {
  const [recovery, setRecovery] = useState(false);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      onSignedIn(await api.signInWithCode(code));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setCode("");
      setBusy(false);
    }
  }

  const ready = recovery ? code.trim().length >= 8 : code.length === 6;

  return (
    <form className="auth-form" onSubmit={(event) => void submit(event)}>
      <p className="auth-hint">
        {recovery
          ? "Type one of the recovery codes you stored when you set up the app. Each works once."
          : "Open your authenticator app and type the six digits it shows for Veyra."}
      </p>
      <div className="auth-as">
        <span>{email}</span>
        <button type="button" className="auth-link" onClick={onRestart}>
          Not you?
        </button>
      </div>
      {recovery ? (
        <label>
          Recovery code
          <input
            className="mono"
            autoComplete="off"
            spellCheck={false}
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase())}
            placeholder="K7QF-29XM"
          />
        </label>
      ) : (
        <CodeField label="Code from the app" value={code} onChange={setCode} autoFocus disabled={busy} />
      )}
      {error && (
        <p className="auth-error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" className="button primary" disabled={busy || !ready}>
        Sign in
      </button>
      <button
        type="button"
        className="auth-link center"
        onClick={() => {
          setRecovery(!recovery);
          setCode("");
          setError(null);
        }}
      >
        {recovery ? "Use the code from the app instead" : "Use a recovery code instead"}
      </button>
    </form>
  );
}
