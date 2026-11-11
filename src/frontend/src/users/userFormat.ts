import type { Role, SignInOutcome, UserEvent } from "../api/types";

/** Colours for avatars, picked from the person's ID so a person keeps theirs. Light enough for white initials. */
const avatarColors = [
  "#2F6FD6",
  "#7C5CD6",
  "#1F9D63",
  "#D9534F",
  "#E69A00",
  "#0E9AA7",
  "#B5651D",
  "#3E8E41",
  "#6B7FD7",
  "#C2548F",
];

/**
 * The initials of a name, for avatars.
 *
 * @param name - The display name.
 * @returns Up to two capital letters.
 */
export function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("");
}

/**
 * The avatar colour of a person.
 *
 * @param id - The person's ID.
 * @returns A CSS colour.
 */
export function avatarColor(id: string): string {
  let hash = 0;

  for (const character of id) {
    hash = (hash * 31 + character.charCodeAt(0)) >>> 0;
  }

  return avatarColors[hash % avatarColors.length] ?? "#2F6FD6";
}

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * When something happened, the way the Users screen says it. Times are UTC, like everywhere in Veyra.
 *
 * @param iso - The time, or null.
 * @param now - The current time.
 * @returns For example `Today 07:41`, `Yesterday 18:02`, `3 days ago`, `Sep 14`, or `Never`.
 */
export function formatWhen(iso: string | null, now: Date): string {
  if (!iso) {
    return "Never";
  }

  const at = new Date(iso);
  const days = Math.floor((startOfDay(now) - startOfDay(at)) / 86_400_000);
  const time = `${String(at.getUTCHours()).padStart(2, "0")}:${String(at.getUTCMinutes()).padStart(2, "0")}`;

  if (days <= 0) {
    return `Today ${time}`;
  }

  if (days === 1) {
    return `Yesterday ${time}`;
  }

  if (days < 30) {
    return `${days} days ago`;
  }

  return `${months[at.getUTCMonth()] ?? ""} ${at.getUTCDate()}`;
}

/**
 * When something started, to read after "since": lower-case today and yesterday with the time, otherwise the date.
 *
 * @param iso - The time.
 * @param now - The current time.
 * @returns For example `today 07:41`, `yesterday 08:15`, or `Oct 12`.
 */
export function formatSince(iso: string, now: Date): string {
  const when = formatWhen(iso, now);

  if (/^(Today|Yesterday)/.test(when)) {
    return when.replace(/^(Today|Yesterday)/, (word) => word.toLowerCase());
  }

  const at = new Date(iso);

  return `${months[at.getUTCMonth()] ?? ""} ${at.getUTCDate()}`;
}

/**
 * Days between a time and now, for the last-sign-in track.
 *
 * @param iso - The time, or null.
 * @param now - The current time.
 * @returns Whole days, or null for never.
 */
export function daysSince(iso: string | null, now: Date): number | null {
  return iso ? Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 86_400_000)) : null;
}

function startOfDay(date: Date): number {
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
}

/** How a sign-in reads in a list, and how it is marked. */
export interface OutcomeText {
  /** The sentence. */
  text: string;
  /** `ok` for signed in, `warn` for a recovery code, `bad` for a failure. */
  tone: "ok" | "warn" | "bad";
}

/**
 * Names how a sign-in attempt ended.
 *
 * @param outcome - The outcome.
 * @returns The sentence and its tone.
 */
export function outcomeText(outcome: SignInOutcome): OutcomeText {
  switch (outcome) {
    case "SignedIn":
      return { text: "Signed in with the password", tone: "ok" };
    case "SignedInWithCode":
      return { text: "Signed in with a code", tone: "ok" };
    case "SignedInWithRecoveryCode":
      return { text: "Signed in with a recovery code", tone: "warn" };
    case "WrongPassword":
      return { text: "Wrong password", tone: "bad" };
    case "WrongCode":
      return { text: "Wrong code", tone: "bad" };
    case "Locked":
      return { text: "Refused: locked", tone: "bad" };
    case "SwitchedOff":
      return { text: "Refused: switched off", tone: "bad" };
  }
}

/**
 * Reads a change to an account as a sentence.
 *
 * @param event - The change.
 * @returns For example `Role Viewer → Editor` or `Second factor removed`.
 */
export function eventText(event: UserEvent): string {
  switch (event.kind) {
    case "Added":
      return event.detail ? `Added as ${event.detail}` : "Added";
    case "RoleChanged": {
      const [from, to] = (event.detail ?? "").split(" ");

      return from && to ? `Role ${from} → ${to}` : "Role changed";
    }
    case "SwitchedOff":
      return "Switched off";
    case "SwitchedOn":
      return "Switched on again";
    case "PasswordChanged":
      return "Password changed";
    case "PasswordReset":
      return "Password reset";
    case "SecondFactorOn":
      return "Second factor switched on";
    case "SecondFactorOff":
      return "Second factor switched off";
    case "SecondFactorRemoved":
      return "Second factor removed";
    case "RecoveryCodesReplaced":
      return "New recovery codes";
    case "SessionsEnded":
      return event.detail === "1" ? "1 session signed out" : `${event.detail ?? "Some"} sessions signed out`;
    case "TeamChanged":
      return event.detail ? `Team set to ${event.detail}` : "Team removed";
  }
}

/** What each role adds, in the words the Users screen uses. */
export const roleReach: Record<Role, string> = {
  Viewer: "reads everything",
  Editor: "also rules, dashboards",
  Admin: "also users, keys",
};

/** The roles from the widest to the innermost. */
export const roles: Role[] = ["Viewer", "Editor", "Admin"];
