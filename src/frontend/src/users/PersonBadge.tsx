import type { UserSummary } from "../api/types";
import { Avatar } from "./Avatar";
import { formatWhen } from "./userFormat";

/** Props for {@link PersonBadge}. */
interface PersonBadgeProps {
  /** The person. */
  person: UserSummary;
  /** Whether the person is the signed-in Admin. */
  isYou: boolean;
  /** Whether the person is open in the panel. */
  selected: boolean;
  /** The current time, for the lock and the last activity. */
  now: Date;
  /** Opens the person in the panel. */
  onSelect(): void;
}

/**
 * One person as a badge: a band in their role's colour, their avatar and name, and whether they use a second factor
 * and when they were last active. A locked badge glows amber; a switched-off one is greyed.
 */
export function PersonBadge({ person, isYou, selected, now, onSelect }: PersonBadgeProps) {
  const locked = person.lockedUntil !== null && new Date(person.lockedUntil) > now;
  const tag = isYou ? "You" : locked ? "Locked" : person.disabled ? "Off" : "";
  const classes = [
    "badge",
    `r-${person.role}`,
    locked ? "hot" : "",
    person.disabled ? "off" : "",
    selected ? "sel" : "",
  ];

  return (
    <button type="button" className={classes.filter(Boolean).join(" ")} aria-pressed={selected} onClick={onSelect}>
      <span className="badge-band">
        <span>{person.role}</span>
        <span>{tag}</span>
      </span>
      <Avatar id={person.id} name={person.displayName} state={person.disabled ? "off" : locked ? "locked" : "active"} />
      <b>{person.displayName}</b>
      <small>{person.email}</small>
      {person.team && <span className="badge-team">{person.team}</span>}
      <span className="badge-chips">
        <span className={person.secondFactorSince ? "on" : person.disabled ? undefined : "miss"}>
          {person.secondFactorSince ? "2FA" : "No 2FA"}
        </span>
        <span>{formatWhen(person.lastActiveAt ?? person.lastSignInAt, now)}</span>
      </span>
    </button>
  );
}
