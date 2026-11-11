import type { AlertDetails, AlertPoint } from "../api/types";

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
