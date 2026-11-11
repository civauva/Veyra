import type { UserSummary } from "../api/types";
import { roleReach } from "./userFormat";

/** Props for {@link RoleBar}. */
interface RoleBarProps {
  /** Everyone. */
  people: UserSummary[];
}

/** The roles as one bar, each part as wide as the number of people in it, Admins first because they reach furthest. */
export function RoleBar({ people }: RoleBarProps) {
  const counts = (["Admin", "Editor", "Viewer"] as const).map(
    (role) => [role, people.filter((person) => person.role === role).length] as const,
  );

  return (
    <div
      className="role-bar"
      role="img"
      aria-label={counts.map(([role, count]) => `${count} ${role}${count === 1 ? "" : "s"}`).join(", ")}
    >
      {counts
        .filter(([, count]) => count > 0)
        .map(([role, count]) => (
          <div key={role} className={`r-${role}`} style={{ flex: count }}>
            {role} {count}
            {role !== "Admin" && <em> · {roleReach[role]}</em>}
          </div>
        ))}
    </div>
  );
}
