import { Link, useSearchParams } from "react-router";

import { rulePath } from "../alerts/rulePath";
import { containerPath } from "../alerts/ruleWords";
import { api } from "../api/client";
import type { HostRow } from "../api/types";
import { useShell } from "../app/shellContext";
import { useIsAdmin } from "../session/useIsAdmin";
import { formatRatio } from "../home/homeFormat";
import { seenWords } from "./resourceFormat";
import { HealthPill, ResourceScreen, Share, type ResourceColumn, type ResourceFilter } from "./ResourceScreen";
import { listQuery } from "./listQuery";
import { useResourcePages } from "./useResourcePages";

const filters: ResourceFilter[] = [
  {
    name: "kind",
    label: "Kind",
    options: [
      { value: "", label: "Hosts and containers" },
      { value: "host", label: "Hosts" },
      { value: "container", label: "Containers" },
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
    name: "state",
    label: "State",
    options: [
      { value: "", label: "Online and silent" },
      { value: "online", label: "Online" },
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
];

/** The columns, with the time the list was drawn for Last seen. */
function columnsAt(now: number): ResourceColumn<HostRow>[] {
  return [
    { title: "Name", cell: (row) => row.name },
    {
      title: "Kind",
      wide: true,
      advanced: true,
      cell: (row) => (
        <span className="rs-muted">
          {row.kind === "host"
            ? `Host${row.os ? ` · ${row.os}` : ""}`
            : `Container${row.parent ? ` · ${row.parent}` : ""}`}
        </span>
      ),
    },
    { title: "Health", cell: (row) => <HealthPill health={row.health} silent={row.silent} name={row.name} /> },
    { title: "CPU", cell: (row) => <Share ratio={row.cpuRatio} /> },
    { title: "Memory", wide: true, cell: (row) => <Share ratio={row.memoryRatio} /> },
    { title: "Fullest disk", wide: true, cell: (row) => <Share ratio={row.fullestDiskRatio} /> },
    {
      title: "Runs",
      wide: true,
      cell: (row) => (
        <span className="rs-muted">
          {row.runsCount === 0 ? (
            "—"
          ) : (
            <>
              {row.runsCount} ·{" "}
              {row.runs.slice(0, 2).map((service, index) => (
                <span key={service}>
                  {index > 0 && ", "}
                  <Link className="rs-cell-link" to={`/metrics/services/${encodeURIComponent(service)}`}>
                    {service}
                  </Link>
                </span>
              ))}
              {row.runsCount > 2 ? " …" : ""}
            </>
          )}
        </span>
      ),
    },
    { title: "Last seen", numeric: true, advanced: true, cell: (row) => seenWords(row.lastSeenAt, now) },
  ];
}

/** A host's screen. */
function hostPath(name: string): string {
  return `/metrics/hosts/${encodeURIComponent(name)}`;
}

/** What a filling disk means and how to fix it, with its open alert (VEY-83). */
function fillingRule(disk: { host: string; mount: string }): string {
  return rulePath("disk-filling", { kind: "host", name: disk.host, part: disk.mount });
}

/**
 * Resources → Hosts & containers (ADR 0022): every machine and container Veyra knows, online or not, with CPU,
 * memory, the fullest disk, and what runs on it. A host that goes silent stays listed, so a dead machine is never
 * missing. Figures and order come from the server.
 */
export function HostsScreen() {
  const [params] = useSearchParams();
  const { status, time } = useShell();
  const query = listQuery(params, time, status.environment, ["kind", "health", "state", "sort", "search"]);
  const list = useResourcePages(query.key, (cursor, signal) =>
    api.resourceHosts({ ...query.request(), cursor }, signal),
  );
  const admin = useIsAdmin();
  const header = list.header;
  const silent = header ? header.known - header.online : 0;
  const [now] = [Date.parse(time?.window().to ?? new Date(0).toISOString())];

  return (
    <ResourceScreen
      title="Hosts & containers"
      actions={
        // Agents watch hosts and their containers; Admins set them up on Settings → Agents (VEY-83).
        admin ? (
          <Link className="button primary" to="/settings?section=Agents">
            Watch a host
          </Link>
        ) : undefined
      }
      intro="Every machine and container Veyra knows, online or not, with what runs on it."
      state={
        header && (silent > 0 || header.disksFilling > 0)
          ? {
              words: `${silent} silent · ${header.disksFilling} disk filling`,
              tone: header.disksFilling > 0 ? "bad" : "warn",
              to: header.disksFillingFirst ? fillingRule(header.disksFillingFirst) : "/resources/hosts?state=silent",
            }
          : null
      }
      figures={
        header
          ? [
              {
                label: "Hosts online",
                value: (
                  <>
                    {header.online} <small>/ {header.known}</small>
                  </>
                ),
                detail: silent > 0 ? `${silent} silent` : "all reporting",
                tone: silent > 0 ? "warn" : undefined,
                help: "Hosts and containers that reported in the last 5 minutes, of every one Veyra has seen while its metrics are kept.",
                to: silent > 0 ? "/resources/hosts?state=silent" : undefined,
              },
              {
                label: "Containers",
                value: header.containers,
                detail: "among them",
                help: "Containers, as their runtime reports them through the agent's Docker receiver.",
                to: "/resources/hosts?kind=container",
              },
              {
                label: "CPU, average",
                value: formatRatio(header.cpuAverage),
                detail: header.cpuBusiest
                  ? `busiest ${header.cpuBusiest.name} ${formatRatio(header.cpuBusiest.ratio)}`
                  : "no CPU reported",
                help: "The average CPU use across the hosts and containers in the time range, and the busiest one.",
                to: header.cpuBusiest ? hostPath(header.cpuBusiest.name) : undefined,
              },
              {
                label: "Memory, average",
                value: formatRatio(header.memoryAverage),
                detail: header.memoryFullest
                  ? `fullest ${header.memoryFullest.name} ${formatRatio(header.memoryFullest.ratio)}`
                  : "no memory reported",
                help: "The average memory use across the hosts and containers, and the fullest one.",
                to: header.memoryFullest ? hostPath(header.memoryFullest.name) : undefined,
              },
              {
                label: "Disks filling",
                value: header.disksFilling,
                detail: header.disksFillingFirst
                  ? `${header.disksFillingFirst.host} ${header.disksFillingFirst.mount} full in ~${header.disksFillingFirst.hoursLeft} h`
                  : "none fills within 30 days",
                tone: header.disksFilling > 0 ? "bad" : undefined,
                help: "Disks whose growth fills them within 30 days, worked out from their last hours; the first to fill is named. Open it for what it means and how to fix it.",
                to: header.disksFillingFirst ? fillingRule(header.disksFillingFirst) : undefined,
              },
            ]
          : []
      }
      filters={filters}
      find="Find a host or container"
      listName="Hosts & containers"
      listHelp="Every host and container, worst first: warnings, cautions, silent ones, then the rest. Open one for its charts and disks."
      columns={columnsAt(now)}
      items={list.items}
      keyOf={(row) => `${row.kind}/${row.name}`}
      linkOf={(row) =>
        row.kind === "host"
          ? hostPath(row.name)
          : row.parent
            ? containerPath({ kind: "container", name: row.name, part: row.parent })
            : `/metrics?search=${encodeURIComponent(row.name)}`
      }
      total={list.total}
      note="Known hosts stay listed when they go silent, so a dead machine is never missing."
      more={list.more}
      loading={list.loading}
      error={list.error}
      onMore={list.loadMore}
    />
  );
}
