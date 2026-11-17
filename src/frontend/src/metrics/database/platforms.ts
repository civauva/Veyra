/** What the agent reads, as it reports it with the login's permissions (VEY-57). */
export type DatabasePlatform = "sqlserver" | "managed-instance" | "azure-sql-database";

/** The tabs a platform does not have, and why, said once under the tabs. */
export interface LeftOut {
  /** The tabs, by their names in the address. */
  tabs: string[];
  /** Why, in one sentence. */
  reason: string;
}

/**
 * The tabs an Azure SQL Database does not have: it is one database, without SQL Server Agent, the server's waits,
 * system_health, or the host. A SQL Server or Managed Instance has every tab.
 *
 * @param platform - The platform, or null before the agent has said.
 * @returns The tabs left out and why, or null for none.
 */
export function tabsLeftOut(platform: string | null | undefined): LeftOut | null {
  if (platform !== "azure-sql-database") {
    return null;
  }

  return {
    tabs: ["waits", "jobs", "events", "server"],
    reason:
      "An Azure SQL Database is one database without SQL Server Agent, server-wide waits, system_health, or a " +
      "host of its own, so Waits, Jobs, Events, and Server are left out. Azure takes its backups.",
  };
}

/**
 * What a tab leaves out on a Managed Instance, where Azure takes the backups and runs the host and the cluster.
 *
 * @param platform - The platform, or null before the agent has said.
 * @param tab - The tab, by its name in the address.
 * @returns A sentence for the tab, or null when nothing is left out.
 */
export function platformNote(platform: string | null | undefined, tab: string): string | null {
  if (platform !== "managed-instance") {
    return null;
  }

  switch (tab) {
    case "databases":
      return "On a Managed Instance, Azure takes the backups and runs the storage, so backups and volumes are left out.";
    case "server":
      return "On a Managed Instance, Azure runs the host and the cluster, so log shipping, cluster nodes, and replicas are left out.";
    default:
      return null;
  }
}
