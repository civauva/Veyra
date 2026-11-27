import { useId } from "react";

import type { SecondFactorPolicy, UserPosture } from "../api/types";
import { HelpTip } from "../ui/HelpTip";

/** Props for {@link Posture}. */
interface PostureProps {
  /** The counts over everyone, made by the server (VEY-67). */
  posture: UserPosture;
  /** Who must use a second factor, or null while it loads. */
  policy: SecondFactorPolicy | null;
  /** Sets who must use a second factor. */
  onPolicy(policy: SecondFactorPolicy): void;
}

/**
 * How well the accounts are protected, at a glance: rings for second factors, recent sign-ins, and Admins with a
 * second factor, next to the rule for who must use one (ADR 0011).
 */
export function Posture({ posture, policy, onPolicy }: PostureProps) {
  const policyName = useId();

  return (
    <section className="posture" aria-label="Security">
      <Gauge
        count={posture.withSecondFactor}
        total={posture.people}
        color="var(--ok-fg)"
        text="use a second factor"
        name="Second factors"
        help={
          "Everyone who signs in with a code from an authenticator app as well as their " +
          "password. A stolen password alone does not open their account."
        }
      />
      <Gauge
        count={posture.recentSignIns}
        total={posture.people}
        color="var(--accent)"
        text="signed in within 30 days"
        name="Recent sign-ins"
        help={
          "Everyone who signed in within the last 30 days. An account nobody uses is still " +
          "a way in; switch it off when its person has left."
        }
      />
      <Gauge
        count={posture.adminsWithSecondFactor}
        total={posture.admins}
        color="var(--metric-violet)"
        text="Admins with a second factor"
        name="Admins with a second factor"
        help={
          "Admins who can sign in and use a second factor, of all such Admins. Admins reach " +
          "furthest, so each of them should have one."
        }
      />
      <fieldset className="posture-policy">
        <legend>Second factor required for</legend>
        <div className="segments">
          {(["Nobody", "Admins", "Everyone"] as const).map((option) => (
            <label key={option} className={policy === option ? "on" : undefined}>
              <input
                type="radio"
                name={policyName}
                value={option}
                checked={policy === option}
                onChange={() => onPolicy(option)}
              />
              {option}
            </label>
          ))}
        </div>
        <p>{policyNote(policy, posture.missing, posture.missingAdmins)}</p>
      </fieldset>
    </section>
  );
}

function policyNote(policy: SecondFactorPolicy | null, missing: number, missingAdmins: number): string {
  if (policy === "Everyone") {
    return missing === 0 ? "Everyone who can sign in has one." : `${people(missing)} set one up at their next sign-in.`;
  }

  if (policy === "Admins") {
    return missingAdmins === 0
      ? `Every Admin has one. Everyone would leave ${people(missing)} to set one up.`
      : `${people(missingAdmins)} set one up at their next sign-in.`;
  }

  return missingAdmins === 0
    ? "Nobody must. Every Admin has one already."
    : `Nobody must. Requiring it for Admins would leave ${people(missingAdmins)} to set one up.`;
}

function people(count: number): string {
  return count === 1 ? "1 active person" : `${count} active people`;
}

/** Props for {@link Gauge}. */
interface GaugeProps {
  /** How many of the total. */
  count: number;
  /** The total. */
  total: number;
  /** The ring's colour. */
  color: string;
  /** What the count is of. */
  text: string;
  /** The ring's name for its question mark, such as "Second factors". */
  name: string;
  /** What the ring counts, behind the question mark. */
  help: string;
}

/** One ring with its count, like the rings on the Metrics cards, and a question mark that explains it (VEY-60). */
function Gauge({ count, total, color, text, name, help }: GaugeProps) {
  const radius = 22;
  const length = 2 * Math.PI * radius;
  const share = total === 0 ? 0 : count / total;

  return (
    <div className="gauge">
      <svg width="56" height="56" viewBox="0 0 56 56" aria-hidden="true">
        <circle cx="28" cy="28" r={radius} fill="none" stroke="var(--chip)" strokeWidth="7" />
        <circle
          cx="28"
          cy="28"
          r={radius}
          fill="none"
          stroke={color}
          strokeWidth="7"
          strokeLinecap="round"
          strokeDasharray={`${length * share} ${length}`}
        />
      </svg>
      <div>
        <b>
          {count}
          <small> of {total}</small>
        </b>
        <span>
          {text}
          <HelpTip label={name}>{help}</HelpTip>
        </span>
      </div>
    </div>
  );
}
