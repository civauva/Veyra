import type { AlertListItem, IncidentContext, IncidentDetails, IncidentItem } from "../api/types";

/*
 * An incident as the server answers it (ADR 0026), shaped after reference screenshot 7: a worker's error rate, its
 * host, and the database it writes to, for the component and end-to-end tests of the incident screens (VEY-76).
 */

const opened = "2026-11-26T14:32:08.000Z";

/** The worker whose writes time out. */
const worker = { kind: "service" as const, name: "CustomerSync", part: null, id: "service:production/CustomerSync" };

/** The database server it writes to. */
const database = {
  kind: "database" as const,
  name: "postgresql pg-01",
  part: null,
  id: "database:production/postgresql pg-01",
};

/** The open incident INC-42. */
export const openIncident: IncidentItem = {
  id: "inc-42",
  number: 42,
  key: "INC-42",
  title: "CustomerSync error rate 8.4%",
  environment: "production",
  severity: "Warning",
  state: "open",
  openedAt: opened,
  acknowledgedAt: null,
  acknowledgedBy: null,
  resolvedAt: null,
  alertCount: 2,
  blocks: [worker, database],
};

/** A resolved one, to show the list in order. */
export const resolvedIncident: IncidentItem = {
  ...openIncident,
  id: "inc-39",
  number: 39,
  key: "INC-39",
  title: "pg-01 blocking for 6 min",
  severity: "Caution",
  state: "resolved",
  openedAt: "2026-11-26T07:40:00.000Z",
  resolvedAt: "2026-11-26T07:46:00.000Z",
  alertCount: 1,
  blocks: [database],
};

/** The alerts of INC-42. */
const alerts: AlertListItem[] = [
  {
    id: "alert-1",
    rule: "error-rate",
    environment: "production",
    block: worker,
    severity: "Warning",
    worst: "Warning",
    value: 0.084,
    peak: 0.112,
    peakAt: "2026-11-26T14:36:00.000Z",
    threshold: 0.05,
    windowSeconds: 300,
    openedAt: opened,
    updatedAt: "2026-11-26T14:40:00.000Z",
    resolvedAt: null,
    area: null,
    incidentId: "inc-42",
    incidentKey: "INC-42",
  },
  {
    id: "alert-2",
    rule: "db-connections",
    environment: "production",
    block: database,
    severity: "Caution",
    worst: "Caution",
    value: 0.94,
    peak: 0.96,
    peakAt: "2026-11-26T14:35:00.000Z",
    threshold: 0.9,
    windowSeconds: 300,
    openedAt: "2026-11-26T14:33:40.000Z",
    updatedAt: "2026-11-26T14:40:00.000Z",
    resolvedAt: null,
    area: "live",
    incidentId: "inc-42",
    incidentKey: "INC-42",
  },
];

/** INC-42 with its alerts, figures, and timeline. */
export const incidentDetails: IncidentDetails = {
  incident: openIncident,
  alerts,
  figures: {
    firstDetectedAt: opened,
    durationSeconds: 12 * 60,
    affectedBlocks: 2,
    leading: { alertId: "alert-1", rule: "error-rate", value: 0.084, threshold: 0.05, peak: 0.112, peakAt: null },
  },
  timeline: [
    {
      at: opened,
      kind: "opened",
      alertId: "alert-1",
      rule: "error-rate",
      block: worker,
      severity: "Warning",
      value: 0.084,
      actor: null,
      deliveries: [
        {
          channelId: "c1",
          channelName: "#ops-alerts",
          channelKind: "Webhook",
          state: "Sent",
          attempts: 1,
          lastAttemptAt: "2026-11-26T14:32:09.000Z",
          lastStatus: 200,
          lastError: null,
          nextAttemptAt: null,
          sentAt: "2026-11-26T14:32:09.000Z",
        },
      ],
    },
    {
      at: "2026-11-26T14:33:40.000Z",
      kind: "opened",
      alertId: "alert-2",
      rule: "db-connections",
      block: database,
      severity: "Caution",
      value: 0.94,
      actor: null,
      deliveries: [],
    },
  ],
  runbooks: [{ rule: "error-rate", url: "https://wiki.example.com/runbooks/error-rate" }],
};

/** What surrounds INC-42. */
export const incidentContext: IncidentContext = {
  from: "2026-11-26T14:02:08.000Z",
  to: "2026-11-26T14:44:08.000Z",
  executionPath: {
    source: "trace",
    traceId: "8fa21c9d4e8f1b2a3c4d5e6f7a8b9c0d",
    jobName: null,
    steps: [
      { name: "SQL Server read", service: "CustomerSync", durationMs: 240, status: "ok", detail: null },
      { name: "Transform", service: "CustomerSync", durationMs: 1800, status: "ok", detail: null },
      { name: "PostgreSQL write", service: "CustomerSync", durationMs: 74_200, status: "error", detail: "timeout" },
    ],
  },
  exception: {
    type: "Npgsql.NpgsqlException",
    message: "Exception while reading from stream",
    stackTrace: "   at Npgsql.Internal.NpgsqlConnector.ReadMessageLong()\n   at Workers.CustomerSync.WriteBatchAsync()",
    service: "CustomerSync",
    traceId: "8fa21c9d4e8f1b2a3c4d5e6f7a8b9c0d",
    source: "trace",
  },
  timeline: [
    {
      at: "2026-11-26T14:29:11.000Z",
      kind: "first-error",
      block: worker,
      title: "First error",
      detail: "PostgreSQL write timed out",
    },
  ],
  charts: [
    {
      title: "Error rate",
      block: worker,
      unit: "ratio",
      lines: [
        {
          label: "Error rate",
          latest: 0.084,
          points: Array.from({ length: 12 }, (_, index) => ({
            at: new Date(Date.parse("2026-11-26T14:02:08.000Z") + index * 210_000).toISOString(),
            value: 0.01 + index * 0.007,
          })),
        },
      ],
    },
  ],
  logs: {
    items: [
      {
        id: 1,
        timestamp: "2026-11-26T14:32:08.421Z",
        observedTimestamp: "2026-11-26T14:32:08.421Z",
        severity: "Error",
        severityText: null,
        body: "PostgreSQL connection timed out",
        serviceName: "CustomerSync",
        environment: "production",
        traceId: "8fa21c9d4e8f1b2a3c4d5e6f7a8b9c0d",
        spanId: null,
        attributes: {},
        resourceAttributes: {},
        hostName: "docker-02",
      },
    ],
    query: "service:CustomerSync AND level>=warning",
    total: 142,
  },
  components: [
    { block: worker, health: "warning", inIncident: true, relation: "affected" },
    { block: database, health: "caution", inIncident: true, relation: "affected" },
    {
      block: { kind: "host", name: "docker-02", part: null, id: "host:production/docker-02" },
      health: "ok",
      inIncident: false,
      relation: "host",
    },
  ],
};
