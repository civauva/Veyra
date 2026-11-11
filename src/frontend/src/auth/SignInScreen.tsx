import { useState, type FormEvent } from "react";

import type { SignInNext } from "../api/types";
import { useSession } from "../session/useSession";
import { AuthLayout } from "./AuthLayout";
import { CodeStep } from "./CodeStep";
import { RequiredSetup } from "./RequiredSetup";

/** Props for {@link SignInScreen}. */
interface SignInScreenProps {
  /** A confirmation to show above the form, for example after first-run setup. */
  notice?: string;
}

/**
 * Signs a person in with a local account: the password, then a code when the account has a second factor, or the
 * setup of one when an Admin requires it (ADR 0011). A failure shows the API's message, which never reveals whether
 * the email exists (ADR 0008).
 */
export function SignInScreen({ notice }: SignInScreenProps) {
  const { signIn, completeSignIn } = useSession();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [next, setNext] = useState<SignInNext | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      const step = await signIn(email, password);

      if (step) {
        setNext(step);
        setPassword("");
        setBusy(false);
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  function restart() {
    setNext(null);
    setPassword("");
  }

  if (next === "code") {
    return (
      <AuthLayout title="Enter your code">
        <CodeStep email={email} onSignedIn={completeSignIn} onRestart={restart} />
      </AuthLayout>
    );
  }

  if (next === "setup") {
    return (
      <AuthLayout title="One more step" wide>
        <RequiredSetup email={email} onSignedIn={completeSignIn} onRestart={restart} />
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="Sign in to Veyra">
      {notice && (
        <p className="auth-notice" role="status">
          {notice}
        </p>
      )}
      <form className="auth-form" onSubmit={(event) => void submit(event)}>
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
