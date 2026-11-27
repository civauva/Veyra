import type { SessionInfo, SignInAttempt, SignInDays as SignInDayCounts } from "../api/types";
import { formatSince, formatWhen, outcomeText } from "./userFormat";

/** Props for {@link SignInDays}. */
interface SignInDaysProps {
  /** Every attempt of the last 14 days, counted per day on the server on the viewer's calendar (VEY-68). */
  counts: SignInDayCounts;
}

/**
 * Sign-in attempts of the last 14 days, one column per day: successful ones rise above the line, failed ones hang
 * below it, so a burst of wrong passwords stands out at once.
 */
export function SignInDays({ counts }: SignInDaysProps) {
  // The tallest column sets the scale, so the chart fits any number of attempts.
  const most = Math.max(1, ...counts.days.map((day) => Math.max(day.signedIn, day.failed)));

  return (
    <div className="signin-days">
      <div
        className="days"
        role="img"
        aria-label={`Last 14 days: ${counts.signedIn} signed in, ${counts.failed} failed`}
      >
        {counts.days.map((day) => (
          <div key={day.day}>
            <span className="up">
              <b style={{ height: `${(day.signedIn / most) * 100}%` }} />
            </span>
            <span className="down">
              <b style={{ height: `${(day.failed / most) * 100}%` }} />
            </span>
          </div>
        ))}
      </div>
      <div className="days-legend">
        <span>14 days ago</span>
        <span>
          <i className="good" /> signed in <i className="bad" /> failed
        </span>
        <span>Today</span>
      </div>
    </div>
  );
}

/** Props for {@link SignInList}. */
interface SignInListProps {
  /** The attempts, newest first. */
  signIns: SignInAttempt[];
  /** The current time. */
  now: Date;
  /** The list's accessible name. */
  label: string;
}

/** Sign-in attempts with how they ended, address, and browser, newest first. */
export function SignInList({ signIns, now, label }: SignInListProps) {
  if (signIns.length === 0) {
    return <p className="person-help">No sign-in attempts in the last 90 days.</p>;
  }

  return (
    <ul className="signin-list" aria-label={label}>
      {signIns.map((attempt, index) => {
        const { text, tone } = outcomeText(attempt.outcome);

        return (
          <li key={`${attempt.at}-${index}`} className={tone}>
            <span className="dot" aria-hidden="true" />
            <div>
              <b>{text}</b>
              <small>
                {[formatWhen(attempt.at, now), attempt.address, attempt.browser].filter(Boolean).join(" · ")}
              </small>
              {attempt.newAddress && <span className="new-address">Address not seen before</span>}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

/** Props for {@link SessionList}. */
interface SessionListProps {
  /** The open sessions. */
  sessions: SessionInfo[];
  /** The current time. */
  now: Date;
  /** Ends a session. */
  onEnd(session: SessionInfo): void;
}

/** Open sessions with their browser, address, and last activity, each with a way to sign it out. */
export function SessionList({ sessions, now, onEnd }: SessionListProps) {
  if (sessions.length === 0) {
    return <p className="person-help">Not signed in anywhere right now.</p>;
  }

  return (
    <ul className="session-list" aria-label="Sessions">
      {sessions.map((session) => {
        const recent = now.getTime() - new Date(session.lastActiveAt).getTime() < 15 * 60_000;

        return (
          <li key={session.id} className={session.current ? "here" : undefined} aria-label={session.browser}>
            <span className="session-icon" aria-hidden="true">
              {/iPhone|Android|iPad/.test(session.browser) ? "▯" : "▭"}
            </span>
            <div>
              <b>
                {session.browser}
                {session.current && <span className="here-tag">this one</span>}
              </b>
              <small>
                {[session.address, `since ${formatSince(session.startedAt, now)}`].filter(Boolean).join(" · ")}
                {session.method === "RecoveryCode" ? ", with a recovery code" : ""}
              </small>
              <span className={recent ? "session-live" : "session-live idle"}>
                {session.current ? "Active now" : `Active ${inSentence(formatWhen(session.lastActiveAt, now))}`}
              </span>
            </div>
            {!session.current && (
              <button type="button" className="button small" onClick={() => onEnd(session)}>
                Sign out
              </button>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/** Lower-cases a leading Today or Yesterday, for use inside a sentence. */
function inSentence(when: string): string {
  return when.replace(/^(Today|Yesterday)/, (word) => word.toLowerCase());
}
