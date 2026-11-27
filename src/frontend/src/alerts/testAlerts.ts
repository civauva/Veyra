import type { AlertContext, AlertDetails, AlertPoint, CatalogRule } from "../api/types";

/*
 * Alerts for tests and the e2e run, matching the VEY-18 mockups: an open warning on payments-gateway with a
 * failing webhook, and a short caution on orders-api that resolved at night.
 */

const opened = Date.UTC(2026, 10, 10, 6, 58);

/** The error ratio per minute around the payments-gateway alert: quiet, a sharp rise at 06:58, a plateau. */
function ratioAt(minutesFromOpening: number): number {
  if (minutesFromOpening < 0) {
    return 0.003;
  }

  const curve = [0.018, 0.03, 0.045, 0.062, 0.09, 0.12, 0.14, 0.16, 0.18, 0.2, 0.21, 0.22, 0.23, 0.236, 0.241];

  return curve[minutesFromOpening] ?? 0.2 + 0.02 * Math.sin(minutesFromOpening / 2);
}

const points: AlertPoint[] = Array.from({ length: 66 }, (_, index) => {
  const minutes = index - 29;
  const total = minutes < 0 ? 610 : 680 + (index % 4) * 10;
  const ratio = ratioAt(minutes);
  const windowTotal = total * 5;

  return {
    at: new Date(opened + minutes * 60_000).toISOString(),
    total,
    errors: Math.round(total * ratio),
    windowTotal,
    windowErrors: Math.round(windowTotal * ratio),
    ratio,
  };
});

/** An open warning, 36 minutes old, with deliveries in every state. */
export const openWarning: AlertDetails = {
  id: "0193a2b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b",
  rule: "error-rate",
  environment: "production",
  block: { kind: "service", name: "payments-gateway", part: null, id: "service:production/payments-gateway" },
  service: "payments-gateway",
  severity: "Warning",
  worst: "Warning",
  value: 0.216,
  peak: 0.241,
  peakAt: "2026-11-10T07:12:00Z",
  threshold: 0.05,
  cautionThreshold: 0.01,
  warningThreshold: 0.05,
  clearThreshold: 0.005,
  lowerIsWorse: false,
  windowSeconds: 300,
  minimumRecords: 20,
  minimumRequests: null,
  openedAt: "2026-11-10T06:58:00Z",
  updatedAt: "2026-11-10T07:34:00Z",
  resolvedAt: null,
  // The server lists each channel told once (VEY-68).
  told: [
    { id: "c1", name: "#ops-alerts" },
    { id: "c2", name: "On-call" },
    { id: "c3", name: "Incident bot" },
  ],
  events: [
    {
      kind: "Opened",
      severity: "Caution",
      value: 0.018,
      at: "2026-11-10T06:58:00Z",
      deliveries: [
        {
          channelId: "c1",
          channelName: "#ops-alerts",
          channelKind: "Webhook",
          state: "Sent",
          attempts: 1,
          lastAttemptAt: "2026-11-10T06:58:01Z",
          lastStatus: 204,
          lastError: null,
          nextAttemptAt: null,
          sentAt: "2026-11-10T06:58:01Z",
        },
      ],
    },
    {
      kind: "Escalated",
      severity: "Warning",
      value: 0.062,
      at: "2026-11-10T07:01:00Z",
      deliveries: [
        {
          channelId: "c1",
          channelName: "#ops-alerts",
          channelKind: "Webhook",
          state: "Sent",
          attempts: 1,
          lastAttemptAt: "2026-11-10T07:01:01Z",
          lastStatus: 204,
          lastError: null,
          nextAttemptAt: null,
          sentAt: "2026-11-10T07:01:01Z",
        },
        {
          channelId: "c2",
          channelName: "On-call",
          channelKind: "Email",
          state: "Sent",
          attempts: 1,
          lastAttemptAt: "2026-11-10T07:01:02Z",
          lastStatus: null,
          lastError: null,
          nextAttemptAt: null,
          sentAt: "2026-11-10T07:01:02Z",
        },
        {
          channelId: "c3",
          channelName: "Incident bot",
          channelKind: "Webhook",
          state: "Pending",
          attempts: 3,
          lastAttemptAt: "2026-11-10T07:04:12Z",
          lastStatus: null,
          lastError: "Timed out after 10 s",
          nextAttemptAt: "2026-11-10T07:12:00Z",
          sentAt: null,
        },
      ],
    },
  ],
  series: {
    bucketSeconds: 60,
    from: "2026-11-10T06:28:00Z",
    to: "2026-11-10T07:34:00Z",
    points,
  },
  topErrors: [
    {
      message: "Timeout calling card-processor after 30000 ms",
      count: 2104,
      first: "2026-11-10T06:58:12Z",
      last: "2026-11-10T07:34:02Z",
    },
    {
      message: "Circuit open for card-processor, request rejected",
      count: 1311,
      first: "2026-11-10T07:00:41Z",
      last: "2026-11-10T07:34:01Z",
    },
    {
      message: "Payment authorisation failed: upstream returned 503",
      count: 512,
      first: "2026-11-10T06:58:30Z",
      last: "2026-11-10T07:33:48Z",
    },
    {
      message: "Retries exhausted, payment left pending",
      count: 188,
      first: "2026-11-10T07:02:05Z",
      last: "2026-11-10T07:31:10Z",
    },
    {
      message: "Card token expired before capture",
      count: 54,
      first: "2026-11-10T07:05:17Z",
      last: "2026-11-10T07:29:40Z",
    },
  ],
  disk: null,
  latency: null,
};

/** A caution that lasted 14 minutes at night and resolved, on a service without an environment. */
export const resolvedCaution: AlertDetails = {
  ...openWarning,
  id: "0193a2b4-0000-7000-8000-000000000002",
  environment: null,
  block: { kind: "service", name: "orders-api", part: null, id: "service:/orders-api" },
  service: "orders-api",
  severity: "Caution",
  worst: "Caution",
  value: 0.004,
  peak: 0.012,
  peakAt: "2026-11-10T03:15:00Z",
  threshold: 0.01,
  openedAt: "2026-11-10T03:10:00Z",
  updatedAt: "2026-11-10T03:24:00Z",
  resolvedAt: "2026-11-10T03:24:00Z",
  // The server lists each channel told once (VEY-68).
  told: [],
  events: [
    { kind: "Opened", severity: "Caution", value: 0.011, at: "2026-11-10T03:10:00Z", deliveries: [] },
    { kind: "Resolved", severity: "Caution", value: 0.004, at: "2026-11-10T03:24:00Z", deliveries: [] },
  ],
  series: { bucketSeconds: 60, from: "2026-11-10T02:40:00Z", to: "2026-11-10T03:34:00Z", points: [] },
  topErrors: [],
};

/** Hourly use of a disk that grows 1.5 points a day, from a week before the alert to now. */
const diskPoints = Array.from({ length: 7 * 24 + 8 }, (_, hour) => ({
  at: new Date(Date.parse("2026-11-03T03:00:00Z") + hour * 3_600_000).toISOString(),
  ratio: hour === 40 ? null : 0.75 + (hour / 24) * 0.015,
}));

/** An open disk-filling caution on web-01, four hours old, told to one webhook. */
export const openDiskFilling: AlertDetails = {
  ...openWarning,
  id: "0193a2b4-0000-7000-8000-000000000003",
  rule: "disk-filling",
  block: { kind: "host", name: "web-01", part: "/", id: "host:production/web-01" },
  service: null,
  severity: "Caution",
  worst: "Caution",
  value: 9.2,
  peak: 9.2,
  peakAt: "2026-11-10T07:30:00Z",
  threshold: 14,
  cautionThreshold: 14,
  warningThreshold: 3,
  clearThreshold: 21,
  lowerIsWorse: true,
  windowSeconds: 86_400,
  minimumRecords: null,
  openedAt: "2026-11-10T03:20:00Z",
  updatedAt: "2026-11-10T07:34:00Z",
  // The server lists each channel told once (VEY-68).
  told: [{ id: "c1", name: "#ops-alerts" }],
  events: [
    {
      kind: "Opened",
      severity: "Caution",
      value: 13,
      at: "2026-11-10T03:20:00Z",
      deliveries: openWarning.events[0]?.deliveries.slice(0, 1) ?? [],
    },
  ],
  series: null,
  topErrors: [],
  disk: {
    mountpoint: "/",
    ratio: 0.86,
    usedBytes: 86e9,
    totalBytes: 100e9,
    daysUntilFull: 9.2,
    bucketSeconds: 3600,
    from: "2026-11-03T03:00:00Z",
    to: "2026-11-10T07:34:00Z",
    points: diskPoints,
    disks: [
      { mountpoint: "/", ratio: 0.86, daysUntilFull: 9.2 },
      { mountpoint: "/var/log", ratio: 0.34, daysUntilFull: null },
      { mountpoint: "/boot", ratio: 0.22, daysUntilFull: null },
    ],
  },
  latency: null,
};

/** One point a minute around a latency alert: normal, slow above 3 s, normal again. */
const latencyPoints = Array.from({ length: 56 }, (_, minute) => {
  const p95 = minute < 20 || minute > 45 ? 0.4 : minute < 27 ? 0.4 + (minute - 20) * 0.4 : 3.2;

  return {
    at: new Date(Date.parse("2026-11-09T21:10:00Z") + minute * 60_000).toISOString(),
    p50Seconds: p95 * 0.3,
    p95Seconds: minute === 5 ? null : p95,
    requests: minute === 5 ? 0 : 600,
  };
});

/** A latency warning of checkout-web that eased and resolved after 25 minutes. */
export const resolvedLatency: AlertDetails = {
  ...openWarning,
  id: "0193a2b4-0000-7000-8000-000000000004",
  rule: "latency-p95",
  block: { kind: "service", name: "checkout-web", part: null, id: "service:production/checkout-web" },
  service: "checkout-web",
  severity: "Caution",
  worst: "Warning",
  value: 0.42,
  peak: 3.4,
  peakAt: "2026-11-09T21:52:00Z",
  threshold: 1,
  cautionThreshold: 1,
  warningThreshold: 3,
  clearThreshold: 0.8,
  minimumRecords: null,
  minimumRequests: 20,
  openedAt: "2026-11-09T21:40:00Z",
  updatedAt: "2026-11-09T22:05:00Z",
  resolvedAt: "2026-11-09T22:05:00Z",
  // The server lists each channel told once (VEY-68).
  told: [],
  events: [
    { kind: "Opened", severity: "Caution", value: 1.3, at: "2026-11-09T21:40:00Z", deliveries: [] },
    { kind: "Escalated", severity: "Warning", value: 3.1, at: "2026-11-09T21:47:00Z", deliveries: [] },
    { kind: "Eased", severity: "Caution", value: 2.1, at: "2026-11-09T22:01:00Z", deliveries: [] },
    { kind: "Resolved", severity: "Caution", value: 0.42, at: "2026-11-09T22:05:00Z", deliveries: [] },
  ],
  series: null,
  topErrors: [],
  disk: null,
  latency: {
    bucketSeconds: 60,
    from: "2026-11-09T21:10:00Z",
    to: "2026-11-09T22:15:00Z",
    points: latencyPoints,
    routes: [
      { method: "POST", route: "/cart/checkout", requestsPerSecond: 41, p95Seconds: 4.1, serverErrorRatio: 0.008 },
      { method: "GET", route: "/cart", requestsPerSecond: 96, p95Seconds: 1.6, serverErrorRatio: 0.001 },
      { method: null, route: "/products/{id}", requestsPerSecond: 210, p95Seconds: 0.38, serverErrorRatio: 0 },
    ],
  },
};

/** The catalog's integrity check rule (ADR 0020), with its guidance (VEY-60), as the API sends it. */
export const integrityRule: CatalogRule = {
  id: "db-checkdb-age",
  name: "Integrity check age",
  description: "Time since a database's last good DBCC CHECKDB, or since its creation when it never had one.",
  unit: "seconds",
  noun: null,
  headline: "{server}: {part} has had no good integrity check for {value}",
  recovery: "{server}: {part} passed an integrity check",
  partLabel: "Database",
  lowerIsWorse: false,
  enabled: true,
  caution: 8 * 86_400,
  warning: 15 * 86_400,
  meaning: "DBCC CHECKDB has not passed on {part} for a while; corruption would go unnoticed.",
  steps: [
    { text: "Run the check now, outside busy hours.", script: "DBCC CHECKDB (N'{part}') WITH NO_INFOMSGS;" },
    { text: "Schedule it weekly in a SQL Server Agent job.", script: null },
  ],
};

/**
 * An open integrity check warning on a database of a developer's PC, as it was first reported in VEY-60: a caution
 * eight days ago, a warning a day ago, measured today. Its rule has no window.
 */
export const openIntegrity: AlertDetails = {
  ...openWarning,
  id: "0193a2b4-0000-7000-8000-000000000060",
  rule: "db-checkdb-age",
  environment: "local",
  block: {
    kind: "database",
    name: "microsoft.sql_server PC-DRAZAN-2023",
    part: "VPower_2.P",
    id: "database:local/microsoft.sql_server PC-DRAZAN-2023",
  },
  service: null,
  severity: "Warning",
  worst: "Warning",
  value: 16 * 86_400 + 9 * 3_600,
  peak: 16 * 86_400 + 9 * 3_600,
  peakAt: "2026-11-25T11:01:00Z",
  threshold: 15 * 86_400,
  cautionThreshold: 8 * 86_400,
  warningThreshold: 15 * 86_400,
  clearThreshold: 8 * 86_400,
  windowSeconds: 0,
  minimumRecords: null,
  openedAt: "2026-11-17T02:05:00Z",
  updatedAt: "2026-11-25T11:01:00Z",
  resolvedAt: null,
  // The server lists each channel told once (VEY-68).
  told: [],
  events: [
    { kind: "Opened", severity: "Caution", value: 8 * 86_400, at: "2026-11-17T02:05:00Z", deliveries: [] },
    { kind: "Escalated", severity: "Warning", value: 15 * 86_400, at: "2026-11-24T02:01:00Z", deliveries: [] },
  ],
  series: null,
  topErrors: [],
  disk: null,
  latency: null,
};

/** An open severe error warning on a developer's PC, with the damaged page it counted (VEY-61). */
export const openSevereError: AlertDetails = {
  ...openIntegrity,
  id: "0193a2b4-0000-7000-8000-000000000061",
  rule: "db-severe-error",
  block: { ...openIntegrity.block, part: null },
  value: 1,
  peak: 1,
  peakAt: "2026-11-25T13:12:00Z",
  threshold: 1,
  cautionThreshold: 1,
  warningThreshold: 1,
  clearThreshold: 1,
  openedAt: "2026-11-25T13:12:00Z",
  updatedAt: "2026-11-25T13:16:00Z",
  // The server lists each channel told once (VEY-68).
  told: [],
  events: [{ kind: "Opened", severity: "Warning", value: 1, at: "2026-11-25T13:12:00Z", deliveries: [] }],
  serverErrors: [
    {
      at: "2026-11-25T13:11:38Z",
      error: 824,
      severity: 24,
      message: "SQL Server detected a logical consistency-based I/O error: incorrect checksum.",
    },
  ],
};

/**
 * What surrounds the payments-gateway warning (ADR 0029): its service failing, the database it calls in caution, the
 * host it runs on, and the warnings and errors they logged from an hour before it opened.
 */
export const openWarningContext: AlertContext = {
  from: "2026-11-10T05:58:00Z",
  to: "2026-11-10T07:34:00Z",
  logs: {
    items: [
      {
        id: 41,
        timestamp: "2026-11-10T07:04:51Z",
        observedTimestamp: "2026-11-10T07:04:51Z",
        severity: "Error",
        severityText: null,
        body: "Payment declined by PSP: upstream timeout after 1800 ms",
        serviceName: "payments-gateway",
        environment: "production",
        traceId: "5b8efff798038103d269b633813fc60c",
        spanId: null,
        attributes: {},
        resourceAttributes: {},
        hostName: "web-02",
      },
      {
        id: 40,
        timestamp: "2026-11-10T06:58:10Z",
        observedTimestamp: "2026-11-10T06:58:10Z",
        severity: "Warning",
        severityText: null,
        body: "Retrying the PSP call, attempt 2 of 3",
        serviceName: "payments-gateway",
        environment: "production",
        traceId: null,
        spanId: null,
        attributes: {},
        resourceAttributes: {},
        hostName: "web-02",
      },
    ],
    query: "service:payments-gateway AND level>=warning",
    total: 31,
  },
  components: [
    { block: openWarning.block, health: "warning", inIncident: true, relation: "affected" },
    {
      block: { kind: "database", name: "postgresql orders-db", part: null, id: "database:production/orders-db" },
      health: "caution",
      inIncident: false,
      relation: "affected",
    },
    {
      block: { kind: "host", name: "web-02", part: null, id: "host:production/web-02" },
      health: "ok",
      inIncident: false,
      relation: "host",
    },
  ],
};
