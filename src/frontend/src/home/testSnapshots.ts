import type { HistogramBucket, HomeBlock, HomeSnapshot } from "../api/types";

/*
 * Home snapshots for unit tests, matching the approved VEY-17 mockups: a storm over Services at 07:34 UTC and the
 * same estate under clear skies. They are test data only; nothing in the app imports them.
 */

const gigabyte = 1024 * 1024 * 1024;

const services = [
  "billing-worker",
  "identity",
  "inventory",
  "notifications",
  "orders-api",
  "payments-gateway",
  "search",
  "shipping",
];

function block(name: string, health: HomeBlock["health"] = "Ok", alertIds: string[] = []): HomeBlock {
  return {
    id: `service:production/${name}`,
    kind: "service",
    name,
    environment: "production",
    health,
    alertIds,
    records: 2400,
    errors: health === "Warning" ? 518 : 2,
    errorRatio: health === "Warning" ? 0.216 : 0.001,
  };
}

/** Records an hour from 08:00 yesterday to 00:00, busy by day and quiet by night, before the last seven hours. */
const earlier = [
  9400, 11200, 12800, 13600, 13900, 13100, 12400, 11800, 12600, 13300, 12100, 9800, 8200, 6900, 5600, 4800, 4400,
];

/**
 * A full day of hours as the API sends it (VEY-24), ending in the hour of the snapshot: the quiet earlier hours, then
 * the last seven with these error shares and 10,000 records each.
 */
function hours(ratios: number[]): HistogramBucket[] {
  const totals = [...earlier, ...ratios.map(() => 10_000)];
  const shares = [...earlier.map(() => 0.0003), ...ratios];

  return totals.map((total, index) => ({
    start: new Date(Date.UTC(2026, 10, 6, 8 + index)).toISOString(),
    total,
    errors: Math.round((shares[index] ?? 0) * total),
  }));
}

const expected = [1.1, 1.4, 1.6, 1.5, null, null].map((load, index) => ({
  start: new Date(Date.UTC(2026, 10, 7, 8 + index)).toISOString(),
  load,
}));

/** A warning on payments-gateway and a caution on billing-worker; 6 of 8 services are dry. */
export const stormSnapshot: HomeSnapshot = {
  environment: "production",
  at: "2026-11-07T07:34:12Z",
  environments: [{ name: "production", condition: "Storm", errorRatio: 0.058 }],
  condition: { kind: "Storm", over: "Services", errorRatio: 0.058 },
  counts: { warning: 1, caution: 1, ok: 6, unknown: 0 },
  alerts: [
    {
      n: 1,
      id: "alert-1",
      block: "service:production/payments-gateway",
      service: "payments-gateway",
      kind: "service",
      name: "payments-gateway",
      part: null,
      environment: "production",
      rule: "error-rate",
      severity: "Warning",
      value: 0.216,
      peak: 0.24,
      threshold: 0.05,
      windowSeconds: 300,
      since: "2026-11-07T06:58:00Z",
      resolvedAt: null,
    },
    {
      n: 2,
      id: "alert-2",
      block: "service:production/billing-worker",
      service: "billing-worker",
      kind: "service",
      name: "billing-worker",
      part: null,
      environment: "production",
      rule: "error-rate",
      severity: "Caution",
      value: 0.013,
      peak: 0.015,
      threshold: 0.01,
      windowSeconds: 300,
      since: "2026-11-07T05:31:00Z",
      resolvedAt: null,
    },
  ],
  resolved: [
    {
      n: null,
      id: "alert-0",
      block: "service:production/orders-api",
      service: "orders-api",
      kind: "service",
      name: "orders-api",
      part: null,
      environment: "production",
      rule: "error-rate",
      severity: "Caution",
      value: 0.002,
      peak: 0.012,
      threshold: 0.01,
      windowSeconds: 300,
      since: "2026-11-07T03:10:00Z",
      resolvedAt: "2026-11-07T03:24:00Z",
    },
  ],
  blocks: services.map((name) =>
    name === "payments-gateway"
      ? block(name, "Warning", ["alert-1"])
      : name === "billing-worker"
        ? block(name, "Caution", ["alert-2"])
        : block(name),
  ),
  metrics: {
    errorRatio: 0.058,
    ingestPerSecond: 66.4,
    records: 19_920,
    windowSeconds: 300,
    latency: { p95Seconds: 0.38, slowestService: "checkout-web", slowestP95Seconds: 0.48, services: 4 },
  },
  hourly: { observed: hours([0.0002, 0.0002, 0.0003, 0.0003, 0.013, 0.004, 0.058]), expected, daysOfHistory: 3 },
  storage: {
    usedBytes: 1.2 * gigabyte,
    budgetBytes: 20 * gigabyte,
    dailyBytes: 1.1 * gigabyte,
    retentionDays: 30,
    daysUntilBudget: 17,
    reachesBudget: true,
  },
  disks: [
    { host: "web-01", mountpoint: "/", ratio: 0.81, daysUntilFull: 23 },
    { host: "db-01", mountpoint: "/data", ratio: 0.58, daysUntilFull: null },
  ],
};

/** The same estate with nothing open. */
export const clearSnapshot: HomeSnapshot = {
  ...stormSnapshot,
  environments: [{ name: "production", condition: "Clear", errorRatio: 0.0004 }],
  condition: { kind: "Clear", over: null, errorRatio: 0.0004 },
  counts: { warning: 0, caution: 0, ok: 8, unknown: 0 },
  alerts: [],
  blocks: services.map((name) => block(name)),
  metrics: { ...stormSnapshot.metrics, errorRatio: 0.0004 },
  hourly: { ...stormSnapshot.hourly, observed: hours([0.0002, 0.0002, 0.0003, 0.0003, 0.0004, 0.0004, 0.0004]) },
  disks: [{ host: "web-01", mountpoint: "/", ratio: 0.47, daysUntilFull: null }],
};
