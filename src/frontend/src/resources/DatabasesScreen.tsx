import { Link, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { DatabaseServerRow } from "../api/types";
import { useShell } from "../app/shellContext";
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

/** Bytes, or a dash when not known. */
const bytes = (value: number | null) => (value === null ? "—" : formatBytes(value));

const columns: ResourceColumn<DatabaseServerRow>[] = [
  { title: "Server", cell: (row) => row.server },
  { title: "Engine", wide: true, cell: (row) => <span className="rs-muted">{row.version ?? row.engine}</span> },
  { title: "Health", cell: (row) => <HealthPill health={row.health} silent={row.silent} /> },
  { title: "Databases", numeric: true, cell: (row) => row.databases },
  { title: "Size", numeric: true, cell: (row) => bytes(row.sizeBytes) },
  {
    title: "Growth / day",
    numeric: true,
    wide: true,
    cell: (row) => (row.growthBytesPerDay === null ? "—" : `+${formatBytes(row.growthBytesPerDay)}`),
  },
  {
    title: "Last full backup",
    wide: true,
    cell: (row) =>
      row.lastFullBackupAgeSeconds === null ? (
        <span className="rs-none">—</span>
      ) : (
        <span className={(row.lateBackups ?? 0) > 0 ? "rs-bad" : undefined}>
          {formatSpan(row.lastFullBackupAgeSeconds)}
          {(row.lateBackups ?? 0) > 0 ? ` · ${row.lateBackups} late` : ""}
        </span>
      ),
  },
  {
    title: "Blocking",
    wide: true,
    cell: (row) =>
      row.blockingChains !== null && row.blockingChains > 0 ? (
        <span className="rs-warn">
          {row.blockingChains} {row.blockingChains === 1 ? "chain" : "chains"}
        </span>
      ) : (
        <span className="rs-none">—</span>
      ),
  },
  { title: "CPU", cell: (row) => <Share ratio={row.cpuRatio} /> },
];

/**
 * Resources → Databases (ADR 0022): every database server Veyra watches, with its databases, size and growth, the
 * last full backup, blocking, and CPU. Figures and order come from the server.
 */
export function DatabasesScreen() {
  const [params] = useSearchParams();
  const { status, time } = useShell();
  const query = listQuery(params, time, status.environment, ["engine", "health", "sort", "system", "search"]);
  const list = useResourcePages(query.key, (cursor, signal) =>
    api.resourceDatabases({ ...query.request(), cursor }, signal),
  );
  const header = list.header;
  const findings = header ? header.lateBackups + (header.blockingChains > 0 ? 1 : 0) : 0;

  return (
    <ResourceScreen
      title="Databases"
      intro="Every database server Veyra watches, with its databases, size, backups, and blocking."
      state={
        header && findings > 0
          ? { words: `${findings} ${findings === 1 ? "finding" : "findings"}`, tone: "warn" }
          : null
      }
      actions={
        <Link className="button primary" to="/settings?section=Databases">
          Add a server
        </Link>
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
              },
              {
                label: "Databases",
                value: header.databases,
                detail: params.get("system") === "true" ? "system databases counted" : "system databases left out",
                help: "Databases on those servers. System databases such as master and msdb are left out unless the filter counts them.",
              },
              {
                label: "Data size",
                value: bytes(header.dataBytes),
                detail:
                  header.growthBytes === null ? "growth not known" : `+${formatBytes(header.growthBytes)} per day`,
                help: "The size of every database together, data and log, and how much they grow per day together.",
              },
              {
                label: "Backups late",
                value: header.lateBackups,
                detail: header.lateBackupFirst
                  ? `${header.lateBackupFirst.database} on ${header.lateBackupFirst.server}`
                  : "every database backed up in time",
                tone: header.lateBackups > 0 ? "warn" : undefined,
                help: "Databases whose last full backup is older than the backup rule allows.",
              },
              {
                label: "Blocking now",
                value: header.blockingChains,
                detail: header.blockingServer ? `chains on ${header.blockingServer}` : "nothing blocked",
                tone: header.blockingChains > 0 ? "warn" : undefined,
                help: "Sessions that hold locks others wait for, now, across the servers.",
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
