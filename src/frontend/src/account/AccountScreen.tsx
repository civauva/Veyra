import { useCallback, useEffect, useId, useState, type FormEvent, type ReactNode } from "react";

import { api } from "../api/client";
import type { Account, Role } from "../api/types";
import { RecoveryCodeList } from "../twofactor/RecoveryCodeList";
import { SetupSteps } from "../twofactor/SetupSteps";
import { Avatar } from "../users/Avatar";
import { closeOnEscape } from "../users/ResetPasswordDialog";
import { SessionList, SignInList } from "../users/SignInViews";
import { formatSince, formatWhen } from "../users/userFormat";
import "../twofactor/twofactor.css";
import "../users/users.css";
import "./account.css";

/** The longest password the length bar measures against; beyond it the bar is full. */
const fullLength = 30;

/** The shortest password Veyra accepts (ADR 0008). */
const minimumLength = 12;

/** What each role may do, for the person's own reach. */
const reach: Record<Role, [boolean, string][]> = {
  Viewer: [
    [true, "Reads all logs, metrics, alerts, and the home"],
    [false, "Alert rules, groupings, and dashboards belong to Editors"],
    [false, "Users, ingestion keys, and notification channels belong to Admins"],
  ],
  Editor: [
    [true, "Reads all logs, metrics, alerts, and the home"],
    [true, "Alert rules, groupings, and dashboards, as they arrive"],
    [false, "Users, ingestion keys, and notification channels belong to Admins: ask one if you need them"],
  ],
  Admin: [
    [true, "Reads all logs, metrics, alerts, and the home"],
    [true, "Alert rules, groupings, and dashboards, as they arrive"],
    [true, "Users, ingestion keys, and notification channels"],
  ],
};

type Dialog = "setup" | "codes" | "off" | null;

/**
 * The signed-in person's own account (ADR 0011): who they are and how far in they reach, their password, their second
 * factor and recovery codes, where they are signed in, and their latest sign-ins. The avatar in the top bar leads
 * here.
 */
export function AccountScreen() {
  const [account, setAccount] = useState<Account | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog>(null);
  const now = new Date();

  const load = useCallback(
    () =>
      api
        .account()
        .then((loaded) => {
          setAccount(loaded);
          setError(null);
        })
        .catch((failure: unknown) => setError(messageOf(failure))),
    [],
  );

  useEffect(() => {
    void load();
  }, [load]);

  async function act(action: () => Promise<unknown>) {
    try {
      await action();
      await load();
    } catch (failure) {
      setError(messageOf(failure));
    }
  }

  function closeDialog() {
    setDialog(null);
    void load();
  }

  return (
    <main className="account">
      <div className="people-head">
        <div>
          <h1>Your account</h1>
          <p>How you sign in, and how far in you reach. Your avatar in the top bar leads here.</p>
        </div>
      </div>

      {error && (
        <p className="people-error" role="alert">
          {error}
        </p>
      )}

      {account && (
        <div className="account-grid">
          <Identity account={account} now={now} />
          {account.hasPassword ? <PasswordForm /> : <ProviderNote />}

          <TwoFactor account={account} now={now} onOpen={setDialog} />

          <section className="account-panel" aria-labelledby="account-sessions">
            <h2 id="account-sessions">Where you are signed in</h2>
            <SessionList
              sessions={account.sessions}
              now={now}
              onEnd={(session) => void act(() => api.endOwnSession(session.id))}
            />
            {account.sessions.length > 1 && (
              <div className="account-right">
                <button type="button" className="button small quiet" onClick={() => void act(api.endOtherSessions)}>
                  Sign out everywhere else
                </button>
              </div>
            )}
          </section>

          <section className="account-panel" aria-labelledby="account-signins">
            <h2 id="account-signins">Recent sign-ins</h2>
            <SignInList signIns={account.signIns} now={now} label="Recent sign-ins" />
            <p className="account-notme">
              Something here that was <b>not you?</b> Change your password, sign out everywhere else, and tell an Admin.
            </p>
          </section>
        </div>
      )}

      {account && dialog === "setup" && (
        <AccountDialog title="Set up two-factor sign-in" onClose={closeDialog} wide>
          <SetupSteps
            account={account.email}
            start={api.startSecondFactor}
            confirm={async (code) => (await api.confirmSecondFactor(code)).recoveryCodes}
            onDone={closeDialog}
            onCancel={closeDialog}
          />
        </AccountDialog>
      )}

      {account && dialog === "codes" && (
        <AccountDialog title="New recovery codes" onClose={closeDialog}>
          <PasswordStep
            account={account.email}
            lead="The old codes stop working at once. Every other browser is signed out."
            action="Make new codes"
            run={async (password) => (await api.newRecoveryCodes(password)).recoveryCodes}
            onDone={closeDialog}
          />
        </AccountDialog>
      )}

      {account && dialog === "off" && (
        <AccountDialog title="Switch off two-factor sign-in" onClose={closeDialog}>
          <PasswordStep
            account={account.email}
            lead="Signing in needs only your password again, and your recovery codes stop working. Every other browser is signed out."
            action="Switch off"
            run={async (password) => {
              await api.switchOffSecondFactor(password);

              return null;
            }}
            onDone={closeDialog}
          />
        </AccountDialog>
      )}
    </main>
  );
}

/** Props for the account's sections. */
interface SectionProps {
  /** The account. */
  account: Account;
  /** The current time. */
  now: Date;
}

/** Who the person is, where they sit in the roles, and a short timeline. */
function Identity({ account, now }: SectionProps) {
  const ring = account.role === "Admin" ? "the core" : account.role === "Editor" ? "the middle ring" : "the outer ring";
  const dot = account.role === "Admin" ? [75, 75] : account.role === "Editor" ? [114, 98] : [131, 108];

  return (
    <section className={`account-panel account-id r-${account.role}`} aria-label="You">
      <div className="account-who">
        <Avatar id={account.id} name={account.displayName} state="active" size="large" />
        <div>
          {account.team && <span className="account-team">{account.team}</span>}
          <b>{account.displayName}</b>
          <small>{account.email}</small>
        </div>
      </div>
      <div className="account-place">
        <svg width="150" height="150" viewBox="0 0 150 150" role="img" aria-label={`${account.role}: ${ring}`}>
          <circle cx="75" cy="75" r="68" className={account.role === "Viewer" ? "ring lit" : "ring viewer"} />
          <circle cx="75" cy="75" r="46" className={account.role === "Editor" ? "ring lit" : "ring editor"} />
          <circle cx="75" cy="75" r="22" className={account.role === "Admin" ? "ring lit core" : "ring admin"} />
          <text x="75" y="17" className="ring-label viewer">
            VIEWER
          </text>
          <text x="75" y="40" className="ring-label editor">
            EDITOR
          </text>
          <text x="75" y="78" className="ring-label admin">
            ADMIN
          </text>
          <circle cx={dot[0]} cy={dot[1]} r="17" className="me-ring" />
          <circle cx={dot[0]} cy={dot[1]} r="13" className="me" />
        </svg>
        <ul className="account-can" aria-label="What your role allows">
          {reach[account.role].map(([yes, text]) => (
            <li key={text} className={yes ? "yes" : "no"}>
              <i aria-hidden="true">{yes ? "✓" : "–"}</i>
              {text}
            </li>
          ))}
        </ul>
      </div>
      <ol className="account-timeline" aria-label="Timeline">
        <li>
          <b>{formatWhen(account.createdAt, now)}</b>
          <span>Account added</span>
        </li>
        <li>
          <b>{formatWhen(account.lastSignInAt, now)}</b>
          <span>Last sign-in</span>
        </li>
        <li className="now">
          <b>Now</b>
          <span>Signed in</span>
        </li>
      </ol>
    </section>
  );
}

/** Changing your own password, with the length that matters shown as a bar. */
function PasswordForm() {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [again, setAgain] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const matches = next.length > 0 && next === again;
  const longEnough = next.length >= minimumLength;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setDone(false);

    try {
      await api.changePassword(current, next);
      setCurrent("");
      setNext("");
      setAgain("");
      setDone(true);
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="account-panel" aria-labelledby="account-password">
      <h2 id="account-password">Change password</h2>
      <form className="account-form" onSubmit={(event) => void submit(event)}>
        <label>
          Current password
          <input
            type="password"
            autoComplete="current-password"
            value={current}
            onChange={(event) => setCurrent(event.target.value)}
          />
        </label>
        <label>
          New password
          <input
            type="password"
            autoComplete="new-password"
            value={next}
            onChange={(event) => setNext(event.target.value)}
          />
        </label>
        <div className="length" aria-hidden="true">
          <span>
            <b style={{ width: `${Math.min(100, (next.length / fullLength) * 100)}%` }} />
            <em style={{ left: `${(minimumLength / fullLength) * 100}%` }} />
          </span>
        </div>
        <p className="account-small" aria-live="polite">
          <b className={longEnough ? "ok" : undefined}>{next.length} characters</b> · at least {minimumLength}. Length
          counts more than symbols; four unrelated words make a strong password you can remember.
        </p>
        <label>
          New password again
          <input
            type="password"
            autoComplete="new-password"
            value={again}
            onChange={(event) => setAgain(event.target.value)}
          />
        </label>
        {again.length > 0 && (
          <p className={matches ? "account-small ok" : "account-small"}>{matches ? "Matches" : "Does not match yet"}</p>
        )}
        <p className="account-info">You stay signed in here. Every other browser and device is signed out.</p>
        {error && (
          <p className="people-error" role="alert">
            {error}
          </p>
        )}
        {done && (
          <p className="account-done" role="status">
            Password changed. Every other browser is signed out.
          </p>
        )}
        <div className="account-right">
          <button type="submit" className="button primary" disabled={busy || !current || !longEnough || !matches}>
            Change password
          </button>
        </div>
      </form>
    </section>
  );
}

/** For accounts that sign in through an identity provider, which handles passwords and second factors. */
function ProviderNote() {
  return (
    <section className="account-panel" aria-labelledby="account-password">
      <h2 id="account-password">Password</h2>
      <p className="account-small">
        You sign in through your organisation&apos;s identity provider. Change your password and second factor there.
      </p>
    </section>
  );
}

/** Props for {@link TwoFactor}. */
interface TwoFactorProps extends SectionProps {
  /** Opens a dialog. */
  onOpen(dialog: Dialog): void;
}

/** Whether a second factor is on, with what can be done about it. */
function TwoFactor({ account, now, onOpen }: TwoFactorProps) {
  const on = account.secondFactor;

  return (
    <section className="account-panel account-full account-tf" aria-labelledby="account-tf">
      <h2 id="account-tf" className="visually-hidden">
        Two-factor sign-in
      </h2>
      <span className={on ? "shield on" : "shield"} aria-hidden="true">
        ⛨
      </span>
      <div className="tf-text">
        <b>{on ? "Two-factor sign-in is on" : "Two-factor sign-in is off"}</b>
        <small>
          {on
            ? `Authenticator app since ${formatSince(on.since, now)} · ${on.recoveryCodesLeft} of 10 recovery codes left · asked for at every sign-in`
            : "A code from your phone at every sign-in, so a stolen password is not enough."}
          {account.secondFactorRequired && " · Your administrator requires it."}
        </small>
      </div>
      {on ? (
        <>
          <button type="button" className="button small" onClick={() => onOpen("codes")}>
            New recovery codes
          </button>
          {!account.secondFactorRequired && (
            <button type="button" className="button small quiet" onClick={() => onOpen("off")}>
              Switch off
            </button>
          )}
        </>
      ) : (
        account.hasPassword && (
          <button type="button" className="button small primary" onClick={() => onOpen("setup")}>
            Set up
          </button>
        )
      )}
    </section>
  );
}

/** Props for {@link AccountDialog}. */
interface AccountDialogProps {
  /** The dialog's heading. */
  title: string;
  /** Closes it. */
  onClose(): void;
  /** Wider, for the QR code. */
  wide?: boolean;
  /** The content. */
  children: ReactNode;
}

/** A dialog over the Account page. Escape or the shade closes it. */
function AccountDialog({ title, onClose, wide = false, children }: AccountDialogProps) {
  const titleId = useId();

  useEffect(() => closeOnEscape(onClose), [onClose]);

  return (
    <>
      <div className="people-shade" aria-hidden="true" onClick={onClose} />
      <div
        className={wide ? "people-dialog wide" : "people-dialog"}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
      >
        <h2 id={titleId}>{title}</h2>
        {children}
      </div>
    </>
  );
}

/** Props for {@link PasswordStep}. */
interface PasswordStepProps {
  /** The account, for the downloaded codes. */
  account: string;
  /** What happens, in one sentence. */
  lead: string;
  /** The button's text. */
  action: string;
  /** Runs the change; resolves to new recovery codes to show, or null. */
  run(password: string): Promise<string[] | null>;
  /** Called when it is done. */
  onDone(): void;
}

/** Asks for the password again before a change to the second factor, then shows new codes if there are any. */
function PasswordStep({ account, lead, action, run, onDone }: PasswordStepProps) {
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      const result = await run(password);

      if (result) {
        setCodes(result);
      } else {
        onDone();
      }
    } catch (failure) {
      setError(messageOf(failure));
      setBusy(false);
    }
  }

  if (codes) {
    return <RecoveryCodeList codes={codes} account={account} onDone={onDone} />;
  }

  return (
    <form className="account-form" onSubmit={(event) => void submit(event)}>
      <p className="account-small">{lead}</p>
      <label>
        Password
        <input
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          autoFocus
        />
      </label>
      {error && (
        <p className="people-error" role="alert">
          {error}
        </p>
      )}
      <div className="people-foot">
        <button type="button" className="button quiet" onClick={onDone}>
          Cancel
        </button>
        <button type="submit" className="button primary" disabled={busy || !password}>
          {action}
        </button>
      </div>
    </form>
  );
}

function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}
