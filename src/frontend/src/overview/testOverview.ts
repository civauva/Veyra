import type { ActivityPoint, HostTrendPoint, IngestionPoint, Overview } from "../api/types";

/** Twenty-four hourly steps ending at 07:00 UTC on 9 November 2026, as the server answers them. */
const hours = Array.from({ length: 24 }, (_, index) => new Date(Date.UTC(2026, 10, 8, 8 + index)).toISOString());

/** A wave, so the charts look like a day of traffic. */
const wave = (index: number, base: number, amplitude: number) => Math.round(base + Math.sin(index / 3.2) * amplitude);

/** The Overview as the server answers it for production, with one failing service and a late backup. */
export const overview: Overview = {
  from: "2026-11-08T08:00:00.000Z",
  to: "2026-11-09T08:00:00.000Z",
  figures: {
    services: { healthy: 4, total: 6, warning: 1, caution: 0, silent: 1 },
    hosts: { online: 6, known: 7 },
    databases: { servers: 4, databases: 26 },
    requests: { count: 1_240_000, change: 0.12 },
    errorRatio: { value: 0.0038, change: 0.0012 },
    openAlerts: { warning: 1, caution: 1 },
    jobs: { succeededRatio: 0.972, runs: 180, failed: 5 },
    availability: { ratio: 0.9995, change: 0.0001, days: 30 },
  },
  activity: {
    stepSeconds: 3600,
    points: hours.map((at, index): ActivityPoint => ({
      at,
      requests: wave(index, 52_000, 18_000),
      errors: index === 20 ? 2_900 : wave(index, 180, 60),
    })),
  },
  ingestion: {
    stepSeconds: 3600,
    points: hours.map((at, index): IngestionPoint => ({
      at,
      logs: wave(index, 41_000, 9_000),
      spans: wave(index, 14_000, 4_000),
      points: wave(index, 6_000, 1_500),
    })),
  },
  hostTrends: {
    stepSeconds: 3600,
    points: hours.map((at, index): HostTrendPoint => ({
      at,
      cpu: wave(index, 38, 12) / 100,
      memory: wave(index, 58, 6) / 100,
      disk: (70 + index * 0.8) / 100,
    })),
  },
  pipelines: [
    {
      name: "CustomerSync",
      server: "SQL-PROD-01",
      engine: "microsoft.sql_server",
      lastRunAt: "2026-11-09T07:57:00.000Z",
      durationSeconds: 12,
      status: "Failed",
      runs: 24,
      failedRuns: 3,
      jobId: "job-customersync",
    },
    {
      name: "OrderExport",
      server: "SQL-PROD-01",
      engine: "microsoft.sql_server",
      lastRunAt: "2026-11-09T07:53:00.000Z",
      durationSeconds: 15,
      status: "Succeeded",
      runs: 24,
      failedRuns: 0,
    },
    {
      name: "NightlyImport",
      server: "SQL-APP-02",
      engine: "microsoft.sql_server",
      lastRunAt: "2026-11-09T07:45:00.000Z",
      durationSeconds: 21,
      status: "Running",
      runs: 1,
      failedRuns: 0,
    },
  ],
  collectors: [
    { name: "Built-in", state: "Online", lastSeenAt: "2026-11-09T07:59:59.000Z", version: "0.1.0", queue: null },
    { name: "branch-office", state: "Offline", lastSeenAt: "2026-11-09T07:54:00.000Z", version: "0.1.0", queue: null },
  ],
  databases: {
    items: [
      {
        name: "Reporting",
        server: "SQL-PROD-01",
        engine: "microsoft.sql_server",
        state: "Online",
        sizeBytes: 290_000_000_000,
        growthBytesPerDay: 600_000_000,
        lastFullBackupAgeSeconds: 111_600,
        late: true,
        connections: null,
      },
      {
        name: "Sales",
        server: "SQL-PROD-01",
        engine: "microsoft.sql_server",
        state: "Online",
        sizeBytes: 359_000_000_000,
        growthBytesPerDay: 240_000_000,
        lastFullBackupAgeSeconds: 25_200,
        late: false,
        connections: null,
      },
      {
        name: "orders",
        server: "pg-01",
        engine: "postgresql",
        state: "Online",
        sizeBytes: 48_000_000_000,
        growthBytesPerDay: 90_000_000,
        lastFullBackupAgeSeconds: null,
        late: false,
        connections: 36,
      },
    ],
    total: 26,
  },
};
