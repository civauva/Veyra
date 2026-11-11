import { useCallback, useEffect, useId, useState } from "react";
import { Link } from "react-router";

import { api } from "../api/client";
import type { Role, UserDetail } from "../api/types";
import { Avatar } from "./Avatar";
import { ResetPasswordDialog } from "./ResetPasswordDialog";
import { SignInDays, SignInList, SessionList } from "./SignInViews";
import { eventText, formatSince, formatWhen, outcomeText, roleReach, roles } from "./userFormat";

/** The tabs of the panel. */
export type PersonTab = "overview" | "signins" | "sessions" | "history";

/** Props for {@link PersonPanel}. */
interface PersonPanelProps {
  /** The person's ID. */
  id: string;
  /** Whether the person is the signed-in Admin, who cannot change their own role or switch themselves off here. */
  isYou: boolean;
  /** The open tab. */
  tab: PersonTab;
  /** Opens a tab. */
  onTab(tab: PersonTab): void;
  /** Closes the panel. */
  onClose(): void;
  /** Called after any change, so the list can refresh. */
  onChanged(): void;
  /** The current time. */
  now: Date;
}

/**
 * One person in detail (ADR 0011): role and team, password, second factor, and whether they can sign in; their
 * sign-ins, open sessions, and the history of the account. A side panel on wide screens, a sheet on smaller ones.
 */
export function PersonPanel({ id, isYou, tab, onTab, onClose, onChanged, now }: PersonPanelProps) {
  const titleId = useId();
  const [detail, setDetail] = useState<UserDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [resetting, setResetting] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [team, setTeam] = useState("");

  // The parent keys the panel by person, so opening someone else starts from an empty panel.
  const load = useCallback(
    () =>
      api
        .user(id)
        .then((loaded) => {
          setDetail(loaded);
          setTeam(loaded.user.team ?? "");
          setError(null);
        })
        .catch((failure: unknown) => setError(messageOf(failure))),
    [id],
  );

  useEffect(() => {
    void load();
  }, [load]);

  async function act(action: () => Promise<unknown>) {
    setError(null);

    try {
      await action();
      await load();
      onChanged();
    } catch (failure) {
      setError(messageOf(failure));
    }
  }

  if (!detail) {
    return (
      <aside className="person-panel" aria-busy="true">
        {error ? (
          <p className="people-error" role="alert">
            {error}
          </p>
        ) : (
          <p className="person-loading">Loading…</p>
        )}
      </aside>
    );
  }

  const person = detail.user;
  const locked = person.lockedUntil !== null && new Date(person.lockedUntil) > now;
  const lockedTime = person.lockedUntil ? formatWhen(person.lockedUntil, now).replace("Today ", "") : "";

  return (
    <section className={`person-panel r-${person.role}`} aria-labelledby={titleId}>
      <button type="button" className="person-close" onClick={onClose}>
        <span aria-hidden="true">×</span>
        <span className="visually-hidden">Close</span>
      </button>
      <header className="person-top">
        <Avatar
          id={person.id}
          name={person.displayName}
          state={person.disabled ? "off" : locked ? "locked" : "active"}
          size="large"
        />
        {person.team && <span className="person-team">{person.team}</span>}
        <h2 id={titleId}>{person.displayName}</h2>
        <small>{person.email}</small>
        {locked && (
          <p className="person-banner">
            <b>Locked until {lockedTime}</b> after too many wrong attempts. A password reset lifts the lock.
          </p>
        )}
        {person.disabled && <p className="person-banner grey">Switched off. Cannot sign in; the history stays.</p>}
      </header>

      <div className="person-tabs" role="tablist" aria-label="Details">
        {(
          [
            ["overview", "Overview", null],
            [
              "signins",
              "Sign-ins",
              detail.signIns.some((one) => one.newAddress && outcomeText(one.outcome).tone === "bad") ? "!" : null,
            ],
            ["sessions", "Sessions", String(detail.sessions.length)],
            ["history", "History", null],
          ] as const
        ).map(([key, label, count]) => (
          <button
            key={key}
            type="button"
            role="tab"
            id={`${titleId}-${key}`}
            aria-selected={tab === key}
            aria-controls={`${titleId}-${key}-pane`}
            className={tab === key ? "on" : undefined}
            onClick={() => onTab(key)}
          >
            {label}
            {count && <em className={count === "!" ? "alarm" : undefined}>{count}</em>}
          </button>
        ))}
      </div>

      {error && (
        <p className="people-error" role="alert">
          {error}
        </p>
      )}

      <div role="tabpanel" id={`${titleId}-${tab}-pane`} aria-labelledby={`${titleId}-${tab}`} className="person-pane">
        {tab === "overview" && (
          <>
            <fieldset className="person-sec person-roles" disabled={isYou}>
              <legend>Role and team</legend>
              {roles.map((role: Role) => (
                <label key={role} className={`r-${role}${person.role === role ? " on" : ""}`}>
                  <input
                    type="radio"
                    name={`${titleId}-role`}
                    checked={person.role === role}
                    onChange={() => void act(() => api.updateUser(person.id, { role }))}
                  />
                  {role}
                  <span>{roleReach[role]}</span>
                </label>
              ))}
              <label className="person-team-field">
                Team
                <input
                  value={team}
                  maxLength={100}
                  placeholder="For example Payments"
                  onChange={(event) => setTeam(event.target.value)}
                  onBlur={() => {
                    if (team.trim() !== (person.team ?? "")) {
                      void act(() => api.updateUser(person.id, { team: team.trim() }));
                    }
                  }}
                  onKeyDown={(event) => event.key === "Enter" && event.currentTarget.blur()}
                />
              </label>
              {isYou && (
                <p className="person-note">
                  This is you. Your own role stays as it is; <Link to="/account">your account</Link> has your password
                  and second factor.
                </p>
              )}
            </fieldset>

            <div className="person-sec">
              <h3>Sign-in</h3>
              <div className="person-line">
                <span className={locked ? "line-icon warn" : "line-icon"} aria-hidden="true">
                  🔑
                </span>
                <div>
                  Password
                  <small>{locked ? "Locked now; a reset lifts the lock" : "Set by the person or an Admin"}</small>
                </div>
                <button type="button" className="button small" onClick={() => setResetting(true)}>
                  Reset password
                </button>
              </div>
              <div className="person-line">
                <span className={detail.secondFactor ? "line-icon ok" : "line-icon"} aria-hidden="true">
                  ⛨
                </span>
                <div>
                  Second factor
                  <small>
                    {detail.secondFactor
                      ? `Authenticator app since ${formatSince(detail.secondFactor.since, now)} · ${detail.secondFactor.recoveryCodesLeft} recovery codes left`
                      : detail.secondFactorRequired
                        ? "Required; set up at the next sign-in"
                        : "Not set up"}
                  </small>
                </div>
                {detail.secondFactor &&
                  (confirmRemove ? (
                    <span className="person-confirm">
                      <button
                        type="button"
                        className="button small danger"
                        onClick={() => {
                          setConfirmRemove(false);
                          void act(() => api.removeSecondFactor(person.id));
                        }}
                      >
                        Remove it
                      </button>
                      <button type="button" className="button small quiet" onClick={() => setConfirmRemove(false)}>
                        Keep
                      </button>
                    </span>
                  ) : (
                    <button type="button" className="button small quiet" onClick={() => setConfirmRemove(true)}>
                      Remove
                    </button>
                  ))}
              </div>
              <div className="person-line">
                <span className="line-icon" aria-hidden="true">
                  ⏻
                </span>
                <div id={`${titleId}-signin`}>
                  Can sign in
                  <small>Switching off ends every session and keeps the history</small>
                </div>
                <button
                  type="button"
                  role="switch"
                  className="switch"
                  aria-checked={!person.disabled}
                  aria-labelledby={`${titleId}-signin`}
                  disabled={isYou}
                  onClick={() => void act(() => api.updateUser(person.id, { disabled: !person.disabled }))}
                />
              </div>
            </div>

            <div className="person-sec">
              <h3>At a glance</h3>
              <dl className="person-facts">
                <dt>Added</dt>
                <dd>
                  {formatWhen(person.createdAt, now)}
                  {detail.addedBy ? `, by ${detail.addedBy}` : ""}
                </dd>
                <dt>Last active</dt>
                <dd>{formatWhen(person.lastActiveAt, now)}</dd>
                <dt>Sessions</dt>
                <dd>{detail.sessions.length === 1 ? "1 open" : `${detail.sessions.length} open`}</dd>
                <dt>Sign-ins, 30 days</dt>
                <dd>
                  {detail.signedInLast30Days} signed in, {detail.failedLast30Days} failed
                </dd>
              </dl>
            </div>
          </>
        )}

        {tab === "signins" && (
          <>
            <div className="person-sec">
              <h3>Last 14 days</h3>
              <SignInDays signIns={detail.signIns} now={now} />
            </div>
            <div className="person-sec">
              <SignInList signIns={detail.signIns} now={now} label="Latest sign-ins" />
            </div>
            <p className="person-help">
              Sign-ins are kept 90 days. Only Admins and {person.displayName.split(/\s+/)[0]} see them.
            </p>
          </>
        )}

        {tab === "sessions" && (
          <>
            <div className="person-sec">
              <SessionList
                sessions={detail.sessions}
                now={now}
                onEnd={(session) => void act(() => api.endUserSession(person.id, session.id))}
              />
            </div>
            {detail.sessions.length > 0 && (
              <div className="person-sec">
                <div className="person-line">
                  <div>
                    Sign {person.displayName.split(/\s+/)[0]} out everywhere
                    <small>Every session ends at once; the password stays.</small>
                  </div>
                  <button
                    type="button"
                    className="button small"
                    onClick={() => void act(() => api.endUserSessions(person.id))}
                  >
                    Sign out all
                  </button>
                </div>
              </div>
            )}
            <p className="person-help">A session ends by itself after 8 hours without activity.</p>
          </>
        )}

        {tab === "history" && (
          <>
            <ol className="person-history" aria-label="History">
              {detail.events.map((event, index) => (
                <li key={`${event.at}-${index}`} className={`ev-${event.kind}`}>
                  <b>{eventText(event)}</b>
                  <small>
                    {formatWhen(event.at, now)}
                    {event.by ? ` · by ${event.by}` : " · by Veyra"}
                  </small>
                </li>
              ))}
            </ol>
            <p className="person-help">Changes to an account are kept as long as the account exists.</p>
          </>
        )}
      </div>

      {resetting && (
        <ResetPasswordDialog
          person={person}
          locked={locked}
          onClose={() => setResetting(false)}
          onDone={() => {
            setResetting(false);
            void load();
            onChanged();
          }}
        />
      )}
    </section>
  );
}

function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}
