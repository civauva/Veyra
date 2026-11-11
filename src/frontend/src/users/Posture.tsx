import { useId } from "react";

import type { SecondFactorPolicy, UserSummary } from "../api/types";
import { daysSince } from "./userFormat";

/** Props for {@link Posture}. */
interface PostureProps {
  /** Everyone. */
  people: UserSummary[];
  /** Who must use a second factor, or null while it loads. */
  policy: SecondFactorPolicy | null;
  /** Sets who must use a second factor. */
  onPolicy(policy: SecondFactorPolicy): void;
  /** The current time, for what counts as recent. */
  now: Date;
}

/**
 * How well the accounts are protected, at a glance: rings for second factors, recent sign-ins, and Admins with a
 * second factor, next to the rule for who must use one (ADR 0011).
 */
export function Posture({ people, policy, onPolicy, now }: PostureProps) {
  const policyName = useId();
  const withFactor = people.filter((person) => person.secondFactorSince).length;
  const recent = people.filter((person) => (daysSince(person.lastSignInAt, now) ?? Infinity) <= 30).length;
  const admins = people.filter((person) => person.role === "Admin" && !person.disabled);
  const adminsWithFactor = admins.filter((person) => person.secondFactorSince).length;
  const missing = people.filter((person) => !person.disabled && !person.secondFactorSince);
  const missingAdmins = missing.filter((person) => person.role === "Admin");

  return (
    <section className="posture" aria-label="Security">
      <Gauge count={withFactor} total={people.length} color="var(--ok-fg)" text="use a second factor" />
      <Gauge count={recent} total={people.length} color="var(--accent)" text="signed in within 30 days" />
      <Gauge
        count={adminsWithFactor}
        total={admins.length}
        color="var(--metric-violet)"
        text="Admins with a second factor"
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
        <p>{policyNote(policy, missing.length, missingAdmins.length)}</p>
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
}

/** One ring with its count, like the rings on the Metrics cards. */
function Gauge({ count, total, color, text }: GaugeProps) {
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
        <span>{text}</span>
      </div>
    </div>
  );
}
