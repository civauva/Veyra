import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";

import { api } from "../api/client";
import { useSession } from "../session/useSession";
import { AuthLayout } from "./AuthLayout";

/**
 * First-run setup: creates the first administrator with the token from the link Veyra wrote to its log (ADR 0008).
 * The token comes from the link itself; without one, the person can paste it.
 */
export function SetupScreen() {
  const { setupCompleted } = useSession();
  const [params] = useSearchParams();
  const tokenFromLink = params.get("token") ?? "";
  const [token, setToken] = useState(tokenFromLink);
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await api.completeSetup({ token, email, displayName, password });
      setupCompleted();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  return (
    <AuthLayout title="Create the first administrator">
      <p className="auth-hint">
        Veyra has no users yet. Open the setup link from Veyra&apos;s log, then choose the administrator&apos;s details.
      </p>
      <form className="auth-form" onSubmit={submit}>
        {!tokenFromLink && (
          <label>
            Setup token
            <input required value={token} onChange={(event) => setToken(event.target.value)} />
          </label>
        )}
        <label>
          Email
          <input
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </label>
        <label>
          Name
          <input autoComplete="name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        </label>
        <label>
          Password
          <input
            type="password"
            autoComplete="new-password"
            required
            minLength={12}
            aria-describedby="password-rule"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
        <span id="password-rule" className="auth-rule">
          At least 12 characters. A few unrelated words work well.
        </span>
        {error && (
          <p className="auth-error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="button primary" disabled={busy}>
          Create administrator
        </button>
      </form>
    </AuthLayout>
  );
}
