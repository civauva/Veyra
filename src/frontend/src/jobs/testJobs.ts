import type { JobDetails, JobDuration, JobList, JobListItem, JobRun, JobRunStatus } from "../api/types";

/*
 * Jobs as the server answers them (ADR 0027), for the component and end-to-end tests of the job screens: a traced ETL
 * worker whose last run failed in its write step, a SQL Server Agent backup, and an export running now.
 */

const from = "2026-11-25T15:00:00.000Z";
const to = "2026-11-26T15:00:00.000Z";

/** The minutes between two runs of CustomerSync. */
const every = 15;

/** The CustomerSync runs that failed, by their place in the window, oldest first. */
const failedAt = new Set([40, 71, 95]);

/** The statuses of CustomerSync's runs in the window, oldest first: one every 15 minutes. */
const syncStatuses: JobRunStatus[] = Array.from({ length: 96 }, (_, index) =>
  failedAt.has(index) ? "failed" : "succeeded",
);

/** The ETL worker CustomerSync, traced through the OpenTelemetry SDK with `job.name`. */
export const customerSync: JobListItem = {
  id: "job-customersync",
  name: "CustomerSync",
  source: "trace",
  block: { kind: "service", name: "CustomerSync", part: null, id: "service:production/CustomerSync" },
  environment: "production",
  lastRun: { startedAt: "2026-11-26T14:45:00.000Z", durationSeconds: 74.2, status: "failed" },
  runs: 96,
  failed: 3,
  successRatio: 93 / 96,
  usualSeconds: 62,
  recent: syncStatuses.slice(-20),
  running: false,
};

/** SQL Server Agent's nightly backup on sql-prod-01. */
export const nightlyBackup: JobListItem = {
  id: "job-nightly-backup",
  name: "Nightly backup",
  source: "sqlagent",
  block: {
    kind: "database",
    name: "microsoft.sql_server sql-prod-01",
    part: null,
    id: "database:production/microsoft.sql_server sql-prod-01",
  },
  environment: "production",
  lastRun: { startedAt: "2026-11-26T02:00:00.000Z", durationSeconds: 2412, status: "succeeded" },
  runs: 1,
  failed: 0,
  successRatio: 1,
  usualSeconds: 2412,
  recent: ["succeeded", "succeeded", "succeeded", "succeeded", "succeeded", "succeeded", "succeeded"],
  running: false,
};

/** The hourly order export, running now. */
export const orderExport: JobListItem = {
  id: "job-orderexport",
  name: "OrderExport",
  source: "trace",
  block: { kind: "service", name: "orders-api", part: null, id: "service:production/orders-api" },
  environment: "production",
  lastRun: { startedAt: "2026-11-26T14:58:00.000Z", durationSeconds: null, status: "running" },
  runs: 24,
  failed: 0,
  successRatio: 1,
  usualSeconds: 312,
  recent: [...Array.from({ length: 19 }, (): JobRunStatus => "succeeded"), "running"],
  running: true,
};

/** Every job, as the server orders them: failing first, then by name. */
export const jobItems: JobListItem[] = [customerSync, nightlyBackup, orderExport];

/** `GET /api/v1/jobs` for the last 24 hours. */
export const jobList: JobList = {
  from,
  to,
  figures: { jobs: 3, runs: 121, failed: 3, successRatio: 117 / 120, running: 1 },
  items: jobItems,
};

/** The start of a CustomerSync run by its place in the window. */
function syncStart(index: number): string {
  return new Date(Date.parse(from) + index * every * 60_000).toISOString();
}

/** How long a CustomerSync run took, by its place: around a minute, longer when it failed. */
function syncSeconds(index: number): number {
  return failedAt.has(index) ? 74.2 : 52 + ((index * 37) % 23) + (index % 5) * 0.4;
}

/** CustomerSync's run durations, oldest first. */
const syncDurations: JobDuration[] = syncStatuses.map((status, index) => ({
  at: syncStart(index),
  seconds: syncSeconds(index),
  status,
}));

/** The steps of a CustomerSync run that succeeded. */
function syncSteps(seconds: number): JobRun["steps"] {
  return [
    { name: "Extract customers", durationSeconds: 12.4, status: "succeeded", message: null },
    { name: "Transform", durationSeconds: 8.1, status: "succeeded", message: null },
    { name: "PostgreSQL write", durationSeconds: seconds - 24.3, status: "succeeded", message: null },
    { name: "Publish events", durationSeconds: 3.8, status: "succeeded", message: null },
  ];
}

/** The latest CustomerSync run: its write timed out after two retries. */
export const failedSyncRun: JobRun = {
  runId: "4bf92f3577b34da6a3ce929d0e0e4736",
  startedAt: syncStart(95),
  durationSeconds: 74.2,
  status: "failed",
  message: "Npgsql.NpgsqlException: Exception while writing to stream (timeout after 30 s)",
  traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
  traceKept: true,
  recordsProcessed: 31_200,
  recordsTotal: 50_000,
  retries: 2,
  steps: [
    { name: "Extract customers", durationSeconds: 12.4, status: "succeeded", message: null },
    { name: "Transform", durationSeconds: 8.1, status: "succeeded", message: null },
    {
      name: "PostgreSQL write",
      durationSeconds: 53.7,
      status: "failed",
      message: "Exception while writing to stream (timeout after 30 s)",
    },
    { name: "Publish events", durationSeconds: null, status: "unknown", message: null },
  ],
};

/** The CustomerSync runs before the latest, newest first; the oldest one's trace has expired. */
const earlierSyncRuns: JobRun[] = [94, 93, 92, 91, 90, 89, 88].map((index, place, all) => ({
  runId: `run-${index}`,
  startedAt: syncStart(index),
  durationSeconds: syncSeconds(index),
  status: "succeeded",
  message: null,
  traceId: `trace${String(index).padStart(27, "0")}`,
  traceKept: place < all.length - 1,
  recordsProcessed: 50_000,
  recordsTotal: 50_000,
  retries: 0,
  steps: syncSteps(syncSeconds(index)),
}));

/** `GET /api/v1/jobs/job-customersync`. */
export const customerSyncDetails: JobDetails = {
  from,
  to,
  job: customerSync,
  figures: {
    runs: 96,
    failed: 3,
    successRatio: 93 / 96,
    medianSeconds: 62,
    p95Seconds: 74.2,
    recordsProcessed: 4_631_200,
  },
  durations: syncDurations,
  runs: [failedSyncRun, ...earlierSyncRuns],
};

/** `GET /api/v1/jobs/job-nightly-backup`: SQL Server Agent's steps, without a trace. */
export const nightlyBackupDetails: JobDetails = {
  from,
  to,
  job: nightlyBackup,
  figures: {
    runs: 1,
    failed: 0,
    successRatio: 1,
    medianSeconds: 2412,
    p95Seconds: 2412,
    recordsProcessed: null,
  },
  durations: [{ at: "2026-11-26T02:00:00.000Z", seconds: 2412, status: "succeeded" }],
  runs: [
    {
      runId: "18342",
      startedAt: "2026-11-26T02:00:00.000Z",
      durationSeconds: 2412,
      status: "succeeded",
      message: "The job succeeded. The Job was invoked by Schedule 4 (Nightly).",
      traceId: null,
      traceKept: false,
      recordsProcessed: null,
      recordsTotal: null,
      retries: 0,
      steps: [
        { name: "Back up databases", durationSeconds: 2280, status: "succeeded", message: null },
        { name: "Verify backups", durationSeconds: 118, status: "succeeded", message: null },
        { name: "Clean up old files", durationSeconds: 14, status: "succeeded", message: null },
      ],
    },
  ],
};

/** `GET /api/v1/jobs/job-orderexport`: a run going on now. */
export const orderExportDetails: JobDetails = {
  from,
  to,
  job: orderExport,
  figures: {
    runs: 24,
    failed: 0,
    successRatio: 1,
    medianSeconds: 312,
    p95Seconds: 340,
    recordsProcessed: 186_400,
  },
  durations: Array.from({ length: 24 }, (_, index) => ({
    at: new Date(Date.parse(from) + index * 3_600_000 + 58 * 60_000).toISOString(),
    seconds: index === 23 ? null : 290 + ((index * 13) % 50),
    status: index === 23 ? "running" : "succeeded",
  })),
  runs: [
    {
      runId: "8a1f0c2d9e3b4f5a6b7c8d9e0f1a2b3c",
      startedAt: "2026-11-26T14:58:00.000Z",
      durationSeconds: null,
      status: "running",
      message: null,
      traceId: "8a1f0c2d9e3b4f5a6b7c8d9e0f1a2b3c",
      traceKept: true,
      recordsProcessed: 4_100,
      recordsTotal: 8_000,
      retries: 0,
      steps: [
        { name: "Read orders", durationSeconds: 41.5, status: "succeeded", message: null },
        { name: "Write CSV", durationSeconds: null, status: "running", message: null },
      ],
    },
  ],
};

/** Each job's answer by its ID, as the server keeps them. */
export const jobDetailsById: Record<string, JobDetails> = {
  [customerSync.id]: customerSyncDetails,
  [nightlyBackup.id]: nightlyBackupDetails,
  [orderExport.id]: orderExportDetails,
};
