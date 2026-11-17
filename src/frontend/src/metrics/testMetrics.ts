import type {
  ChartLine,
  ContainerCard,
  DatabaseCard,
  DatabaseMetrics,
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
 * services, one with server errors; the SQL Server of the VEY-31 mockup, whose shop database has no backup for
 * 31 hours and whose nightly backup job failed; and three Docker containers, one restarting near its memory limit.
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

/** The shop's API container on web-01: calm, up for three days. */
export const shopApi: ContainerCard = {
  name: "shop-api",
  host: "web-01",
  environment: "production",
  image: "ghcr.io/example/shop-api:1.4.2",
  id: "4f2c9a1b7e3d",
  lastSeen: "2026-11-10T11:00:10Z",
  cpuPercent: 12.5,
  memoryBytes: 512 * 1024 ** 2,
  memoryLimitBytes: 1024 ** 3,
  memoryRatio: 0.5,
  restarts: 0,
  restartsTotal: 0,
  uptimeSeconds: 3 * 86_400 + 4 * 3600,
  cpu: line("cpu", 12.5, 3, 2),
};

/** The shop's worker on web-01: near its memory limit, and restarted three times in the range. */
export const shopWorker: ContainerCard = {
  ...shopApi,
  name: "shop-worker",
  image: "ghcr.io/example/shop-worker:1.4.2",
  id: "9b81c0de55f2",
  cpuPercent: 148,
  memoryBytes: 476 * 1024 ** 2,
  memoryLimitBytes: 512 * 1024 ** 2,
  memoryRatio: 0.93,
  restarts: 3,
  restartsTotal: 7,
  uptimeSeconds: 240,
  cpu: line("cpu", 140, 20, 5),
};

/** The containers of web-01, by name. */
export const web01Containers: ContainerCard[] = [shopApi, shopWorker];

/** A cache on web-02, without a memory limit of its own. */
const redis: ContainerCard = {
  ...shopApi,
  name: "redis",
  host: "web-02",
  image: "redis:7.4-alpine",
  id: "1d2e3f4a5b6c",
  cpuPercent: 0.8,
  memoryBytes: 38 * 1024 ** 2,
  memoryLimitBytes: 16 * 1024 ** 3,
  memoryRatio: 0.0023,
  restarts: null,
  restartsTotal: null,
  uptimeSeconds: 11 * 86_400,
  cpu: line("cpu", 0.8, 0.2, 7),
};

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
  databases: [],
  containers: [...web01Containers, redis],
};

/** The SQL Server of the VEY-31 mockup. */
export const sqlProd: DatabaseCard = {
  engine: "microsoft.sql_server",
  server: "sql-prod-01",
  environment: "production",
  lastSeen: "2026-11-10T11:00:12Z",
  cpus: 8,
  memoryBytes: 52 * 1024 ** 3,
  uptimeSeconds: 41 * 86_400,
  batchRequests: 1240,
  connections: 182,
  blocked: 0,
  deadlocks: 2,
  pageLifeSeconds: 3412,
  longestQuerySeconds: 134,
  databases: 5,
  oldestBackupSeconds: 31 * 3600,
  failedJobs: 1,
  requests: line("batch requests", 1200, 140, 3),
  maxConnections: null,
  commitsPerSecond: null,
  rollbacksPerSecond: null,
  wraparoundRatio: null,
};

/** The overview with the SQL Server among the hosts and services. */
export const overviewWithDatabase: MetricsOverview = { ...overview, databases: [sqlProd] };

/** What only a PostgreSQL database has, for a SQL Server's databases. */
const noPostgresParts = {
  connections: null,
  transactionIdAge: null,
  wraparoundRatio: null,
  liveRows: null,
  deadRows: null,
  deadRowsRatio: null,
  vacuums: null,
};

/** A PostgreSQL server near its connection limit, with a database halfway to transaction ID wraparound (VEY-33). */
export const pgMain: DatabaseCard = {
  engine: "postgresql",
  server: "pg-main",
  environment: "production",
  lastSeen: "2026-11-10T11:00:12Z",
  cpus: null,
  memoryBytes: null,
  uptimeSeconds: null,
  batchRequests: null,
  connections: 86,
  blocked: 2,
  deadlocks: 1,
  pageLifeSeconds: null,
  longestQuerySeconds: 412,
  databases: 3,
  oldestBackupSeconds: null,
  failedJobs: 0,
  requests: line("transactions", 640, 90, 4),
  maxConnections: 100,
  commitsPerSecond: 631.4,
  rollbacksPerSecond: 2.6,
  wraparoundRatio: 0.52,
};

/** The overview with the PostgreSQL server beside the SQL Server. */
export const overviewWithPostgres: MetricsOverview = { ...overview, databases: [pgMain, sqlProd] };

/** The PostgreSQL server in detail. */
export const postgresMetrics: DatabaseMetrics = {
  card: pgMain,
  range: "6h",
  bucketSeconds: 300,
  requests: pgMain.requests,
  connections: line("connections", 84, 6, 2),
  blocked: line("blocked", 1, 1, 1),
  deadlocks: line("deadlocks", 0.05, 0.05, 5),
  pageLife: line("page life", 0, 0, 1),
  longestQuery: line("longest query", 200, 180, 6),
  waits: [],
  databases: [
    {
      name: "shop",
      sizeBytes: 412e9,
      logBytes: null,
      growthBytesPerDay: 3.1e9,
      backupAgeSeconds: null,
      ioLatencySeconds: null,
      longestQuerySeconds: 412,
      connections: 61,
      transactionIdAge: 1_116_691_497,
      wraparoundRatio: 0.52,
      liveRows: 48_200_000,
      deadRows: 3_900_000,
      deadRowsRatio: 0.0749,
      vacuums: 14,
    },
    {
      name: "reporting",
      sizeBytes: 96e9,
      logBytes: null,
      growthBytesPerDay: 0.2e9,
      backupAgeSeconds: null,
      ioLatencySeconds: null,
      longestQuerySeconds: 41,
      connections: 22,
      transactionIdAge: 214_748_365,
      wraparoundRatio: 0.1,
      liveRows: 9_100_000,
      deadRows: 12_000,
      deadRowsRatio: 0.0013,
      vacuums: 2,
    },
    {
      name: "postgres",
      sizeBytes: 7.6e6,
      logBytes: null,
      growthBytesPerDay: null,
      backupAgeSeconds: null,
      ioLatencySeconds: null,
      longestQuerySeconds: 0,
      connections: 3,
      transactionIdAge: 98_000_000,
      wraparoundRatio: 0.0456,
      liveRows: null,
      deadRows: null,
      deadRowsRatio: null,
      vacuums: null,
    },
  ],
  jobs: [],
  running: [],
  topQueries: [],
  agentWork: null,
  maxConnections: line("maximum", 100, 0, 1),
  commits: line("commits", 630, 90, 4),
  rollbacks: line("rollbacks", 2.6, 1, 3),
  tables: [
    {
      database: "shop",
      schema: "public",
      name: "carts",
      liveRows: 1_200_000,
      deadRows: 2_800_000,
      deadRowsRatio: 0.7,
      vacuums: 0,
    },
    {
      database: "shop",
      schema: "public",
      name: "orders",
      liveRows: 31_000_000,
      deadRows: 900_000,
      deadRowsRatio: 0.0282,
      vacuums: 12,
    },
    {
      database: "reporting",
      schema: "sales",
      name: "daily_totals",
      liveRows: 400_000,
      deadRows: 12_000,
      deadRowsRatio: 0.0291,
      vacuums: 2,
    },
  ],
};

/** The SQL Server in detail. */
export const databaseMetrics: DatabaseMetrics = {
  card: sqlProd,
  range: "6h",
  bucketSeconds: 300,
  requests: sqlProd.requests,
  connections: line("connections", 180, 12, 2),
  blocked: line("blocked", 0, 0, 1),
  deadlocks: line("deadlocks", 0.1, 0.1, 5),
  pageLife: line("page life", 3400, 60, 4),
  longestQuery: line("longest query", 40, 30, 6),
  waits: [line("CPU", 0.41, 0.05, 1), line("Lock", 0.22, 0.08, 2), line("Disk I/O", 0.18, 0.03, 3)],
  databases: [
    {
      name: "shop_archive",
      sizeBytes: 1.21e12,
      logBytes: 8e9,
      growthBytesPerDay: 0.4e9,
      backupAgeSeconds: 7 * 3600,
      ioLatencySeconds: 0.0068,
      ...noPostgresParts,
      longestQuerySeconds: 0,
    },
    {
      name: "shop",
      sizeBytes: 412e9,
      logBytes: 12e9,
      growthBytesPerDay: 3.1e9,
      backupAgeSeconds: 31 * 3600,
      ioLatencySeconds: 0.0042,
      ...noPostgresParts,
      longestQuerySeconds: 134,
    },
    {
      name: "reporting",
      sizeBytes: 96e9,
      logBytes: 2e9,
      growthBytesPerDay: 1.2e9,
      backupAgeSeconds: null,
      ioLatencySeconds: 0.0055,
      ...noPostgresParts,
      longestQuerySeconds: 41,
    },
    {
      name: "tempdb",
      sizeBytes: 24e9,
      logBytes: 1e9,
      growthBytesPerDay: null,
      backupAgeSeconds: null,
      ioLatencySeconds: 0.0008,
      ...noPostgresParts,
      longestQuerySeconds: 0,
    },
  ],
  jobs: [
    { name: "Nightly full backup - shop", failed: true, lastRun: "2026-11-10T01:00:00Z", durationSeconds: 4324 },
    { name: "Index maintenance", failed: false, lastRun: "2026-11-10T00:00:00Z", durationSeconds: 1341 },
    { name: "Purge sessions", failed: false, lastRun: "2026-11-10T10:00:00Z", durationSeconds: 4 },
  ],
  running: [
    {
      session: "117",
      database: "shop",
      user: "app_shop",
      elapsedSeconds: 134,
      status: "suspended",
      wait: "LCK_M_U",
      blockedBy: "73",
      text: "UPDATE dbo.Orders SET Status = @status WHERE CustomerId = @customerId AND Status = @old",
    },
    {
      session: "203",
      database: "reporting",
      user: "bi_reader",
      elapsedSeconds: 41,
      status: "running",
      wait: null,
      blockedBy: null,
      text: "SELECT c.Region, SUM(o.Total) FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId",
    },
  ],
  topQueries: [
    {
      text: "EXEC dbo.MonthlyReport @month",
      totalSeconds: 80,
      executions: 12,
      workerSeconds: 61,
      logicalReads: 9_120_000,
      database: "reporting",
    },
    {
      text: "SELECT * FROM dbo.Orders WHERE CustomerId = @id",
      totalSeconds: 12.5,
      executions: 400,
      workerSeconds: 9.4,
      logicalReads: 180_000,
      database: "shop",
    },
  ],
  agentWork: { statements: 12, totalSeconds: 4.2 },
  maxConnections: null,
  commits: null,
  rollbacks: null,
  tables: [],
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
  containers: web01Containers,
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
