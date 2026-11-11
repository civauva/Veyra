import { useEffect, useState, type FormEvent } from "react";

import type { SecondFactorSetup } from "../api/types";
import { CodeField } from "./CodeField";
import { QrCode } from "./QrCode";
import { RecoveryCodeList } from "./RecoveryCodeList";
import "./twofactor.css";

/** Props for {@link SetupSteps}. */
interface SetupStepsProps {
  /** The account the app will list, the email address. */
  account: string;
  /** Asks Veyra for a new secret. */
  start(): Promise<SecondFactorSetup>;
  /** Sends the code the app shows now; resolves to the recovery codes, or rejects with a message. */
  confirm(code: string): Promise<string[]>;
  /** Called after the person stored the recovery codes. */
  onDone(): void;
  /** Called when the person backs out before the second factor is on. */
  onCancel(): void;
  /** The text of the back-out button: "Cancel" on the Account page, "Sign out" during sign-in. */
  cancelLabel?: string;
}

type Step = "scan" | "confirm" | "codes";

/** Authenticator apps people use, so they know what "an authenticator app" means. */
const apps = ["Microsoft Authenticator", "Google Authenticator", "1Password", "Bitwarden", "Aegis"];

/**
 * Sets up a second factor in three steps (ADR 0011): scan the QR code, confirm with a code from the app, and store
 * the recovery codes. The factor is switched on only when a code works.
 */
export function SetupSteps({ account, start, confirm, onDone, onCancel, cancelLabel = "Cancel" }: SetupStepsProps) {
  const [step, setStep] = useState<Step>("scan");
  const [setup, setSetup] = useState<SecondFactorSetup | null>(null);
  const [code, setCode] = useState("");
  const [codes, setCodes] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    start().then(
      (started) => !cancelled && setSetup(started),
      (failure: unknown) => !cancelled && setError(messageOf(failure)),
    );

    return () => {
      cancelled = true;
    };
  }, [start]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      setCodes(await confirm(code));
      setStep("codes");
    } catch (failure) {
      setError(messageOf(failure));
      setCode("");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="setup-steps">
      <ol className="stepper" aria-label="Steps">
        {(["scan", "confirm", "codes"] as const).map((one, index) => (
          <li
            key={one}
            className={one === step ? "on" : index < ["scan", "confirm", "codes"].indexOf(step) ? "done" : undefined}
            aria-current={one === step ? "step" : undefined}
          >
            {index + 1} · {one === "scan" ? "Scan" : one === "confirm" ? "Confirm" : "Recovery codes"}
          </li>
        ))}
      </ol>

      {error && (
        <p className="twofactor-error" role="alert">
          {error}
        </p>
      )}

      {step !== "codes" && setup && (
        <div className="setup-pair">
          <QrCode value={setup.uri} label={`QR code for ${account}`} />
          {step === "scan" ? (
            <div className="setup-side">
              <p>Scan the code with an authenticator app on your phone.</p>
              <ul className="setup-apps" aria-label="Apps that work">
                {apps.map((app) => (
                  <li key={app}>{app}</li>
                ))}
              </ul>
              <p className="setup-small">Cannot scan? Type this key instead:</p>
              <code className="setup-secret">{setup.secret.replace(/(.{4})/g, "$1 ").trim()}</code>
            </div>
          ) : (
            <form className="setup-side" id="setup-confirm" onSubmit={(event) => void submit(event)}>
              <p>Type the code your app shows now. Veyra switches the second factor on only once a code works.</p>
              <CodeField label="Code from the app" value={code} onChange={setCode} autoFocus disabled={busy} />
            </form>
          )}
        </div>
      )}

      {step === "scan" && (
        <div className="twofactor-foot">
          <button type="button" className="button quiet" onClick={onCancel}>
            {cancelLabel}
          </button>
          <button type="button" className="button primary" disabled={!setup} onClick={() => setStep("confirm")}>
            Next: enter a code
          </button>
        </div>
      )}

      {step === "confirm" && (
        <div className="twofactor-foot">
          <button type="button" className="button quiet" onClick={() => setStep("scan")}>
            Back
          </button>
          <button type="submit" form="setup-confirm" className="button primary" disabled={busy || code.length !== 6}>
            Confirm
          </button>
        </div>
      )}

      {step === "codes" && <RecoveryCodeList codes={codes} account={account} onDone={onDone} />}
    </div>
  );
}

function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}
