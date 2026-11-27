import type { UserList } from "../api/types";
import { roleReach } from "./userFormat";

/** Props for {@link RoleBar}. */
interface RoleBarProps {
  /** How many people hold each role, as the server counts them (VEY-67). */
  roles: UserList["roles"];
}

/** The roles as one bar, each part as wide as the number of people in it, Admins first because they reach furthest. */
export function RoleBar({ roles }: RoleBarProps) {
  const counts = [
    ["Admin", roles.admins],
    ["Editor", roles.editors],
    ["Viewer", roles.viewers],
  ] as const;

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
