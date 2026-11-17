import type { PermissionsView } from "../../api/types";
import { FindingItem } from "./StorageTab";

/** Props for {@link PermissionNotice}. */
export interface PermissionNoticeProps {
  /** What the monitoring login lacks on the server, or null before it is known. */
  permissions: PermissionsView | null;
  /** The tab shown, by its name in the address. */
  tab: string;
  /** The server, for the words. */
  server: string;
}

/**
 * Says, above a tab, what Veyra cannot read on it because the monitoring login lacks a permission, with the statements
 * that grant it (VEY-58). Without it, a tab the login may not read looks the same as one with nothing to show. Shows
 * nothing when the login may read the whole tab, or before the agent has reported.
 *
 * @returns The notice, or null.
 */
export function PermissionNotice({ permissions, tab, server }: PermissionNoticeProps) {
  const gaps = blindingGaps(permissions, tab);

  if (permissions === null || gaps.length === 0) {
    return null;
  }

  const login = permissions.login ?? "The monitoring login";
  const reads = [...new Set(gaps.map((gap) => gap.reads))];

  return (
    <ul className="live-findings" aria-label="What Veyra cannot read">
      <FindingItem
        finding={{
          kind: "permission",
          severity: "warning",
          title: `Veyra cannot read ${reads.join(", ")} on ${server}`,
          detail:
            `${login} lacks ${gaps.map((gap) => gap.permission).join(", ")}. ` +
            "Until it has them, this tab stays empty where it needs them, and cannot tell whether all is well.",
          fix: "Run this as an administrator on the server. The agent checks again within 5 minutes.",
          script: permissions.script,
        }}
      />
    </ul>
  );
}

/**
 * The permissions that leave part of a tab empty.
 *
 * @param permissions - What the login lacks, or null before it is known.
 * @param tab - The tab, by its name in the address.
 * @returns The gaps that blind the tab; none before the agent has reported.
 */
export function blindingGaps(permissions: PermissionsView | null, tab: string) {
  return permissions?.gaps.filter((gap) => gap.tabs.includes(tab)) ?? [];
}
