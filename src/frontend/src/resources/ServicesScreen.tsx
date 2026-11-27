import { Link, useSearchParams } from "react-router";

import { api } from "../api/client";
import type { ServiceRow } from "../api/types";
import { useShell } from "../app/shellContext";
import { useIsAdmin } from "../session/useIsAdmin";
import { formatRatio } from "../home/homeFormat";
import { formatSeconds } from "../metrics/metricFormat";
import { changeTone, changeWords, shortCount } from "./resourceFormat";
import { HealthPill, ResourceScreen, type ResourceColumn, type ResourceFilter } from "./ResourceScreen";
import { listQuery } from "./listQuery";
import { MiniLine } from "./MiniLine";
import { useResourcePages } from "./useResourcePages";

const filters: ResourceFilter[] = [
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
];

const columns: ResourceColumn<ServiceRow>[] = [
  { title: "Service", cell: (row) => row.name },
  { title: "Health", cell: (row) => <HealthPill health={row.health} silent={row.silent} name={row.name} /> },
  { title: "Req / min", numeric: true, cell: (row) => shortCount(row.requestsPerMinute) },
  { title: "p95", numeric: true, cell: (row) => formatSeconds(row.p95Seconds) },
  {
    title: "Errors",
    numeric: true,
    cell: (row) => (
      <Link
        className={row.health === "Warning" ? "rs-bad rs-cell-link" : "rs-cell-link"}
        to={`/traces?${new URLSearchParams({ service: row.name, show: "failed" }).toString()}`}
        aria-label={`Failed traces of ${row.name}: ${formatRatio(row.errorRatio)} errors`}
      >
        {formatRatio(row.errorRatio)}
      </Link>
    ),
  },
  { title: "Instances", numeric: true, wide: true, advanced: true, cell: (row) => row.instances },
  {
    title: "Runs on",
    wide: true,
    advanced: true,
    cell: (row) => (
      <span className="rs-muted">
        {row.hosts.length === 0
          ? "—"
          : row.hosts.map((host, index) => (
              <span key={host}>
                {index > 0 && ", "}
                <Link className="rs-cell-link" to={`/metrics/hosts/${encodeURIComponent(host)}`}>
                  {host}
                </Link>
              </span>
            ))}
      </span>
    ),
  },
  {
    title: "Requests",
    wide: true,
    advanced: true,
    cell: (row) => <MiniLine values={row.requests} tone={row.health === "Warning" ? "bad" : "ok"} />,
  },
];

/**
 * Resources → Services (ADR 0022): every service that sends telemetry, worst first, with its requests, latency,
 * errors, instances, and where it runs; services that went silent stay listed. Figures and order come from the server.
 */
export function ServicesScreen() {
  const [params] = useSearchParams();
  const { status, time } = useShell();
  const query = listQuery(params, time, status.environment, ["health", "sort", "search"]);
  const list = useResourcePages(query.key, (cursor, signal) =>
    api.resourceServices({ ...query.request(), cursor }, signal),
  );
  const admin = useIsAdmin();
  const header = list.header;
  const troubled = header ? header.warning + header.caution : 0;

  return (
    <ResourceScreen
      title="Services"
      actions={
        // A service sends its telemetry with an ingestion key; Admins make one on Ingestion keys (VEY-83).
        admin ? (
          <Link className="button primary" to="/keys">
            Connect a service
          </Link>
        ) : undefined
      }
      intro="Every service that sends telemetry, worst first. Health comes from the alert rules."
      state={
        header && troubled > 0
          ? {
              words: `${header.warning} warning · ${header.caution} caution`,
              tone: header.warning > 0 ? "bad" : "warn",
              to: `/alerts?${new URLSearchParams({ state: "open" }).toString()}`,
            }
          : null
      }
      figures={
        header
          ? [
              {
                label: "Services healthy",
                value: (
                  <>
                    {header.healthy} <small>/ {header.total}</small>
                  </>
                ),
                detail: `${header.warning} warning · ${header.caution} caution · ${header.silent} silent`,
                tone: header.warning > 0 ? "bad" : troubled > 0 ? "warn" : undefined,
                help: "Services without an open alert, of every service the filters keep. Silent services sent nothing for 2 hours.",
                to:
                  header.warning > 0
                    ? "/resources/services?health=warning"
                    : header.caution > 0
                      ? "/resources/services?health=caution"
                      : header.silent > 0
                        ? "/resources/services?health=silent"
                        : undefined,
              },
              {
                label: "Requests",
                value: shortCount(header.requests),
                detail: (
                  <span className={`rs-${changeTone(header.requestsChange, false)}`}>
                    {changeWords(header.requestsChange)}
                  </span>
                ),
                help: "HTTP requests the services answered in the time range, against the period of the same length before it.",
                to: "/traces",
              },
              {
                label: "Error rate",
                value: formatRatio(header.errorRatio),
                detail: (
                  <span className={`rs-${changeTone(header.errorRatioChange, true)}`}>
                    {changeWords(header.errorRatioChange, true)}
                  </span>
                ),
                tone: changeTone(header.errorRatioChange, true) === "bad" ? "bad" : undefined,
                help: "The share of requests answered with a server error (5xx) in the time range, and its change in percentage points.",
                to: "/traces?show=failed",
              },
              {
                label: "p95 latency",
                value: formatSeconds(header.p95Seconds),
                detail: (
                  <span className={`rs-${changeTone(header.p95Change, true)}`}>{changeWords(header.p95Change)}</span>
                ),
                help: "95 of every 100 requests were answered faster than this, across the services, in the time range.",
                to: "/traces?show=slow&order=worst",
              },
              {
                label: "Instances",
                value: header.instances,
                detail: `on ${header.hosts} ${header.hosts === 1 ? "host" : "hosts"}`,
                help: "Running copies of the services, from their service.instance.id, and the hosts they run on.",
                to: "/resources/hosts",
              },
            ]
          : []
      }
      filters={filters}
      find="Find a service"
      listName="Services"
      listHelp="Every service, worst first: warnings, cautions, silent ones, then the rest. Open one for its requests, errors, and traces."
      columns={columns}
      items={list.items}
      keyOf={(row) => `${row.environment ?? ""}/${row.name}`}
      linkOf={(row) => `/metrics/services/${encodeURIComponent(row.name)}`}
      total={list.total}
      note="A service is silent when nothing arrived for 2 hours."
      more={list.more}
      loading={list.loading}
      error={list.error}
      onMore={list.loadMore}
    />
  );
}
