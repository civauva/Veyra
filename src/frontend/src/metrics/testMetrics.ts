import type {
  ChartLine,
  HostCard,
  HostMetrics,
  MetricChart,
  MetricNameInfo,
  MetricsOverview,
  ServiceCard,
  ServiceMetrics,
} from "../api/types";

/*
 * Metrics for tests and the e2e run, matching the VEY-19 mockups: three hosts, one with a filling disk, and three
 * services, one with server errors.
 */

const at = "2026-11-10T11:00:20Z";

/** A line of 72 five-minute points ending at 11:00, around a base value with a gentle wave. */
function line(label: string, base: number, amplitude: number, seed = 1): ChartLine {
  const points = Array.from({ length: 72 }, (_, index) => ({
    at: new Date(Date.UTC(2026, 10, 10, 5, 5) + index * 300_000).toISOString(),
    value: Math.max(0, base + amplitude * Math.sin(index / 7 + seed) + amplitude * 0.4 * Math.sin(index / 2.3 + seed)),
  }));

  return { label, latest: points.at(-1)?.value ?? null, points };
}

const disk = (mountpoint: string, ratio: number, totalGb: number, daysUntilFull: number | null) => ({
  mountpoint,
  device: "/dev/sda1",
  usedBytes: ratio * totalGb * 1024 ** 3,
  totalBytes: totalGb * 1024 ** 3,
  ratio,
  daysUntilFull,
});

/** The web host with a disk that fills in about 23 days. */
export const web01: HostCard = {
  name: "web-01",
  environment: "production",
  os: "Ubuntu 24.04",
  cpus: 4,
  memoryBytes: 16 * 1024 ** 3,
  lastSeen: "2026-11-10T11:00:08Z",
  cpuRatio: 0.42,
  memoryRatio: 0.68,
  load: 1.6,
  fullestDisk: disk("/", 0.81, 100, 23),
  fillingDisk: disk("/", 0.81, 100, 23),
  cpu: line("cpu", 0.42, 0.08, 3),
};

const hosts: HostCard[] = [
  web01,
  {
    ...web01,
    name: "web-02",
    cpuRatio: 0.18,
    memoryRatio: 0.54,
    load: 0.7,
    fullestDisk: disk("/", 0.47, 100, null),
    fillingDisk: null,
    cpu: line("cpu", 0.2, 0.05, 5),
  },
  {
    ...web01,
    name: "db-01",
    os: "Debian 13",
    cpus: 8,
    memoryBytes: 32 * 1024 ** 3,
    cpuRatio: 0.63,
    memoryRatio: 0.91,
    load: 4.9,
    fullestDisk: disk("/data", 0.58, 500, null),
    fillingDisk: null,
    cpu: line("cpu", 0.6, 0.1, 8),
  },
];

/** The payments service with server errors. */
export const payments: ServiceCard = {
  name: "payments-gateway",
  environment: "production",
  runtime: ".NET",
  instances: 3,
  lastSeen: "2026-11-10T11:00:15Z",
  requestsPerSecond: 118,
  p95Seconds: 0.34,
  serverErrorRatio: 0.021,
  cpuRatio: 0.38,
  memoryBytes: 1.2 * 1024 ** 3,
  latency: line("p95", 0.27, 0.04, 2),
};

const services: ServiceCard[] = [
  payments,
  {
    ...payments,
    name: "checkout-web",
    runtime: "Node.js",
    instances: 2,
    requestsPerSecond: 210,
    p95Seconds: 0.48,
    serverErrorRatio: 0.004,
    cpuRatio: 0.22,
    memoryBytes: 640 * 1024 ** 2,
    latency: line("p95", 0.44, 0.05, 4),
  },
  {
    ...payments,
    name: "orders-api",
    instances: 2,
    requestsPerSecond: 64,
    p95Seconds: 0.12,
    serverErrorRatio: 0.001,
    cpuRatio: 0.09,
    memoryBytes: 410 * 1024 ** 2,
    latency: line("p95", 0.11, 0.015, 6),
  },
];

/** The overview of the mockup. */
export const overview: MetricsOverview = {
  at,
  range: "6h",
  bucketSeconds: 300,
  usage: {
    series: 1284,
    maxSeries: 50_000,
    pointsPerSecond: 38,
    usedBytes: 412 * 1024 ** 2,
    retentionDays: 30,
    names: 412,
  },
  hosts,
  services,
};

/** The web host in detail. */
export const hostMetrics: HostMetrics = {
  card: web01,
  range: "6h",
  bucketSeconds: 300,
  cpu: [
    line("total", 0.42, 0.08, 3),
    line("user", 0.3, 0.06, 3),
    line("system", 0.09, 0.02, 7),
    line("wait", 0.03, 0.01, 9),
  ],
  memory: [line("used", 0.68, 0.03, 4), line("cache", 0.22, 0.02, 9)],
  disks: [disk("/", 0.81, 100, 23), disk("/var/log", 0.34, 20, null), disk("/boot", 0.22, 1, null)],
  network: [line("received", 14e6, 4e6, 2), line("sent", 22e6, 5e6, 5)],
  diskIo: [line("read", 3.1e6, 1e6, 6), line("written", 9.4e6, 3e6, 8)],
  load: [line("1m", 1.6, 0.3), line("5m", 1.4, 0.2), line("15m", 1.2, 0.1)],
};

/** The payments service in detail. */
export const serviceMetrics: ServiceMetrics = {
  card: payments,
  range: "6h",
  bucketSeconds: 300,
  instanceNames: ["web-01", "web-02", "web-03"],
  latency: [line("p50", 0.092, 0.01, 4), line("p95", 0.34, 0.04, 2), line("p99", 0.56, 0.07, 3)],
  requests: [line("ok", 110, 22, 5), line("4xx", 6, 2, 7), line("5xx", 2.5, 0.5, 8)],
  routes: [
    { method: "POST", route: "/checkout/confirm", requestsPerSecond: 58, p95Seconds: 0.52, serverErrorRatio: 0.042 },
    { method: "GET", route: "/payments/{id}", requestsPerSecond: 35, p95Seconds: 0.088, serverErrorRatio: 0.001 },
    { method: "POST", route: "/refunds", requestsPerSecond: 16, p95Seconds: 0.31, serverErrorRatio: 0.006 },
  ],
  resources: [line("cpu", 0.38, 0.06, 2), line("memory", 1.2 * 1024 ** 3, 0.05 * 1024 ** 3, 9)],
  runtime: [line("gc", 4, 1.5, 3), line("queue", 2, 0.6, 6), line("threads", 62, 4, 1)],
};

/** Metric names matching `http.server`. */
export const names: MetricNameInfo[] = [
  {
    name: "http.server.active_requests",
    kind: "Sum",
    unit: "{request}",
    description: "Active requests",
    series: 7,
    sources: ["payments-gateway", "orders-api"],
  },
  {
    name: "http.server.request.duration",
    kind: "Histogram",
    unit: "s",
    description: "Duration of HTTP server requests.",
    series: 41,
    sources: ["payments-gateway", "orders-api", "checkout-web"],
  },
];

/** The request duration chart split by service. */
export const durationChart: MetricChart = {
  name: "http.server.request.duration",
  kind: "Histogram",
  unit: "s",
  show: "p95",
  split: "service.name",
  splitKeys: ["host.name", "http.request.method", "http.response.status_code", "http.route", "service.name"],
  bucketSeconds: 300,
  lines: [
    line("checkout-web", 0.44, 0.05, 4),
    line("payments-gateway", 0.27, 0.04, 2),
    line("orders-api", 0.11, 0.015, 6),
  ],
};
