import { avatarColor, initialsOf } from "./userFormat";

/** Props for {@link Avatar}. */
interface AvatarProps {
  /** The person's ID, which picks the colour. */
  id: string;
  /** The person's name, for the initials. */
  name: string;
  /** The state dot: active, locked, or switched off; none when left out. */
  state?: "active" | "locked" | "off";
  /** The size class: `small` for lists, `large` for the person panel and the Account page. */
  size?: "small" | "medium" | "large";
}

/** A person's initials on their colour, with a dot for whether they can sign in. Decorative: names go next to it. */
export function Avatar({ id, name, state, size = "medium" }: AvatarProps) {
  return (
    <span
      className={`avatar ${size}${state ? ` dot-${state}` : ""}`}
      style={{ background: avatarColor(id) }}
      aria-hidden="true"
    >
      {initialsOf(name)}
    </span>
  );
}
