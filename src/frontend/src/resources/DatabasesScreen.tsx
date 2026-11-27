import { Link, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { DatabaseServerRow } from "../api/types";
import { useShell } from "../app/shellContext";
import { useIsAdmin } from "../session/useIsAdmin";
import { formatBytes } from "../home/homeFormat";
import { formatSpan } from "../metrics/metricFormat";
import { HealthPill, ResourceScreen, Share, type ResourceColumn, type ResourceFilter } from "./ResourceScreen";
import { listQuery } from "./listQuery";
import { useResourcePages } from "./useResourcePages";

const filters: ResourceFilter[] = [
  {
    name: "engine",
    label: "Engine",
    options: [
      { value: "", label: "All" },
      { value: "microsoft.sql_server", label: "SQL Server" },
      { value: "postgresql", label: "PostgreSQL" },
    ],
  },
  {
    name: "health",
    label: "Health",
    options: [
      { value: "", label: "Any" },
      { value: "warning", label: "Warning" },
      { value: "caution", label: "Caution" },
      { value: "ok", label: "Healthy" },
      { value: "silent", label: "Silent" },
    ],
  },
  {
    name: "sort",
    label: "Sort",
    options: [
      { value: "worst", label: "Worst first" },
      { value: "name", label: "By name" },
    ],
  },
  {
    name: "system",
    label: "System databases",
    options: [
      { value: "", label: "Left out" },
      { value: "true", label: "Counted" },
    ],
  },
];

/**
 * A tab of a database server's screen, where a figure or a cell of the list has its details (VEY-83).
 *
 * @param engine - The engine, as `db.system.name` spells it.
 * @param server - The server.
 * @param tab - The tab, such as `backups`.
 * @returns The path.
 */
function serverTab(engine: string, server: string, tab: string): string {
  return `/metrics/databases/${encodeURIComponent(engine)}/${encodeURIComponent(server)}?tab=${tab}`;
}

/** Bytes, or a dash when not known. */
const bytes = (value: number | null) => (value === null ? "—" : formatBytes(value));

const columns: ResourceColumn<DatabaseServerRow>[] = [
  { title: "Server", cell: (row) => row.server },
  {
    title: "Engine",
    wide: true,
    advanced: true,
    cell: (row) => <span className="rs-muted">{row.version ?? row.engine}</span>,
  },
  { title: "Health", cell: (row) => <HealthPill health={row.health} silent={row.silent} name={row.server} /> },
  { title: "Databases", numeric: true, cell: (row) => row.databases },
  { title: "Size", numeric: true, cell: (row) => bytes(row.sizeBytes) },
  {
    title: "Growth / day",
    numeric: true,
    wide: true,
    advanced: true,
    cell: (row) => (row.growthBytesPerDay === null ? "—" : `+${formatBytes(row.growthBytesPerDay)}`),
  },
  {
    title: "Last full backup",
    wide: true,
    cell: (row) =>
      row.lastFullBackupAgeSeconds === null ? (
        <span className="rs-none">—</span>
      ) : (
        <Link
          className={(row.lateBackups ?? 0) > 0 ? "rs-bad rs-cell-link" : "rs-cell-link"}
          to={serverTab(row.engine, row.server, "backups")}
        >
          {formatSpan(row.lastFullBackupAgeSeconds)}
          {(row.lateBackups ?? 0) > 0 ? ` · ${row.lateBackups} late` : ""}
        </Link>
      ),
  },
  {
    title: "Blocking",
    wide: true,
    cell: (row) =>
      row.blockingChains !== null && row.blockingChains > 0 ? (
        <Link className="rs-warn rs-cell-link" to={serverTab(row.engine, row.server, "blocking")}>
          {row.blockingChains} {row.blockingChains === 1 ? "chain" : "chains"}
        </Link>
      ) : (
        <span className="rs-none">—</span>
      ),
  },
  {
    title: "CPU",
    cell: (row) => (
      <Link className="rs-cell-link" to={serverTab(row.engine, row.server, "live")} aria-label={`CPU of ${row.server}`}>
        <Share ratio={row.cpuRatio} />
      </Link>
    ),
  },
];

/**
 * Resources → Databases (ADR 0022): every database server Veyra watches, with its databases, size and growth, the
 * last full backup, blocking, and CPU. Figures and order come from the server. Every figure, finding, and cell with
 * more behind it opens it: the databases on the Overview, a server's Backups, Blocking, or Live tab (VEY-83).
 */
export function DatabasesScreen() {
  const [params] = useSearchParams();
  const { status, time } = useShell();
  const query = listQuery(params, time, status.environment, ["engine", "health", "sort", "system", "search"]);
  const list = useResourcePages(query.key, (cursor, signal) =>
    api.resourceDatabases({ ...query.request(), cursor }, signal),
  );
  const header = list.header;
  const admin = useIsAdmin();
  const findings = header ? header.lateBackups + (header.blockingChains > 0 ? 1 : 0) : 0;
  // The header names a server, not its engine; the list knows it, and backups are SQL Server's alone.
  const engineOf = (server: string) =>
    list.items.find((row) => row.server === server)?.engine ?? "microsoft.sql_server";
  const lateTab = header?.lateBackupFirst
    ? serverTab(engineOf(header.lateBackupFirst.server), header.lateBackupFirst.server, "backups")
    : undefined;
  const blockingTab = header?.blockingServer
    ? serverTab(engineOf(header.blockingServer), header.blockingServer, "blocking")
    : undefined;

  return (
    <ResourceScreen
      title="Databases"
      intro="Every database server Veyra watches, with its databases, size, backups, and blocking."
      state={
        header && findings > 0
          ? {
              words: `${findings} ${findings === 1 ? "finding" : "findings"}`,
              tone: "warn",
              to: lateTab ?? blockingTab,
            }
          : null
      }
      actions={
        // Only Admins may add a server; the page it opens is theirs (VEY-83).
        admin ? (
          <Link className="button primary" to="/settings?section=Databases">
            Add a server
          </Link>
        ) : undefined
      }
      figures={
        header
          ? [
              {
                label: "Servers",
                value: header.servers,
                detail: header.silent > 0 ? `${header.silent} silent` : "all reporting",
                tone: header.silent > 0 ? "warn" : undefined,
                help: "Database servers Veyra watches through its agents, and how many sent nothing lately.",
                to: header.silent > 0 ? "/resources/databases?health=silent" : undefined,
              },
              {
                label: "Databases",
                value: header.databases,
                detail: params.get("system") === "true" ? "system databases counted" : "system databases left out",
                help: "Databases on those servers. System databases such as master and msdb are left out unless the filter counts them.",
                to: "/#databases",
              },
              {
                label: "Data size",
                value: bytes(header.dataBytes),
                detail:
                  header.growthBytes === null ? "growth not known" : `+${formatBytes(header.growthBytes)} per day`,
                help: "The size of every database together, data and log, and how much they grow per day together.",
                to: "/#databases",
              },
              {
                label: "Backups late",
                value: header.lateBackups,
                detail: header.lateBackupFirst
                  ? `${header.lateBackupFirst.database} on ${header.lateBackupFirst.server}`
                  : "every database backed up in time",
                tone: header.lateBackups > 0 ? "warn" : undefined,
                help: "Databases whose last full backup is older than the backup rule allows.",
                to: lateTab,
              },
              {
                label: "Blocking now",
                value: header.blockingChains,
                detail: header.blockingServer ? `chains on ${header.blockingServer}` : "nothing blocked",
                tone: header.blockingChains > 0 ? "warn" : undefined,
                help: "Sessions that hold locks others wait for, now, across the servers.",
                to: blockingTab,
              },
            ]
          : []
      }
      filters={filters}
      find="Find a server"
      listName="Database servers"
      listHelp="Every database server, worst first. Open one for its live sessions, queries, waits, blocking, storage, and jobs."
      columns={columns}
      items={list.items}
      keyOf={(row) => `${row.engine}/${row.server}`}
      linkOf={(row) => `/metrics/databases/${encodeURIComponent(row.engine)}/${encodeURIComponent(row.server)}`}
      total={list.total}
      note="Open a server for its live sessions, queries, waits, blocking, storage, and jobs."
      more={list.more}
      loading={list.loading}
      error={list.error}
      onMore={list.loadMore}
    />
  );
}
