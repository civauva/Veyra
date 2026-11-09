import { useState, type FormEvent } from "react";

import { useSession } from "../session/useSession";
import { AuthLayout } from "./AuthLayout";

/** Props for {@link SignInScreen}. */
interface SignInScreenProps {
  /** A confirmation to show above the form, for example after first-run setup. */
  notice?: string;
}

/**
 * Signs a person in with a local account. A failure shows the API's message, which never reveals whether the email
 * exists (ADR 0008).
 */
export function SignInScreen({ notice }: SignInScreenProps) {
  const { signIn } = useSession();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await signIn(email, password);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  return (
    <AuthLayout title="Sign in to Veyra">
      {notice && (
        <p className="auth-notice" role="status">
          {notice}
        </p>
      )}
      <form className="auth-form" onSubmit={submit}>
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
          Password
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
        {error && (
          <p className="auth-error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="button primary" disabled={busy}>
          Sign in
        </button>
      </form>
    </AuthLayout>
  );
}
