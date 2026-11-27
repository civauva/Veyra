import type {
  DatabaseServerRow,
  DatabasesHeader,
  HostRow,
  HostsHeader,
  ResourcePage,
  ServiceRow,
  ServicesHeader,
} from "../api/types";

/** A service, healthy unless said otherwise. */
function service(name: string, extra: Partial<ServiceRow> = {}): ServiceRow {
  return {
    name,
    environment: "production",
    health: "Ok",
    silent: false,
    requestsPerMinute: 1020,
    p95Seconds: 0.132,
    errorRatio: 0.0006,
    instances: 3,
    hosts: ["web-01", "web-02"],
    lastSeenAt: "2026-11-09T07:34:00Z",
    requests: [22, 25, 24, 30, 28, 34, 31, 36, 33, 30, 29, 35],
    ...extra,
  };
}

/** The Services list as the server answers it: worst first. */
export const servicesList: ResourcePage<ServicesHeader, ServiceRow> = {
  header: {
    total: 6,
    healthy: 4,
    warning: 1,
    caution: 0,
    silent: 1,
    requests: 1_240_000,
    requestsChange: 0.12,
    errorRatio: 0.0038,
    errorRatioChange: 0.0012,
    p95Seconds: 0.214,
    p95Change: 0,
    instances: 14,
    hosts: 6,
  },
  items: [
    service("payments-gateway", {
      health: "Warning",
      errorRatio: 0.041,
      p95Seconds: 0.742,
      requestsPerMinute: 510,
      hosts: ["api-01"],
      requests: [20, 24, 22, 40, 52, 61, 58, 66, 70, 64, 72, 80],
    }),
    service("import-worker", {
      silent: true,
      requestsPerMinute: null,
      p95Seconds: null,
      errorRatio: null,
      instances: 0,
      hosts: [],
      requests: [],
    }),
    service("orders-api"),
    service("identity-api", { requestsPerMinute: 1190, p95Seconds: 0.145 }),
    service("inventory-api", { requestsPerMinute: 850, p95Seconds: 0.119, hosts: ["api-02"] }),
    service("gateway", { requestsPerMinute: 1870, p95Seconds: 0.097, hosts: ["proxy-01"] }),
  ],
  total: 6,
  next: null,
};

/** A host, healthy and online unless said otherwise. */
function host(name: string, extra: Partial<HostRow> = {}): HostRow {
  return {
    name,
    kind: "host",
    os: "Linux",
    parent: null,
    health: "Ok",
    silent: false,
    cpuRatio: 0.21,
    memoryRatio: 0.47,
    fullestDiskRatio: 0.38,
    runs: ["orders-api"],
    runsCount: 1,
    lastSeenAt: "2026-11-09T07:34:10Z",
    ...extra,
  };
}

/** The Hosts & containers list: the filling disk first, the silent machine still there. */
export const hostsList: ResourcePage<HostsHeader, HostRow> = {
  header: {
    known: 7,
    online: 6,
    containers: 2,
    cpuAverage: 0.31,
    cpuBusiest: { name: "payments-5c2", ratio: 0.71 },
    memoryAverage: 0.58,
    memoryFullest: { name: "payments-5c2", ratio: 0.82 },
    disksFilling: 1,
    disksFillingFirst: { host: "sql-01", mount: "D:", hoursLeft: 38 },
  },
  items: [
    host("sql-01", {
      os: "Windows",
      health: "Warning",
      cpuRatio: 0.24,
      memoryRatio: 0.51,
      fullestDiskRatio: 0.89,
      runs: ["SQL-PROD-01"],
    }),
    host("payments-5c2", {
      kind: "container",
      os: null,
      parent: "docker-01",
      health: "Caution",
      cpuRatio: 0.71,
      memoryRatio: 0.82,
      fullestDiskRatio: null,
      runs: ["payments-gateway"],
    }),
    host("branch-01", {
      silent: true,
      cpuRatio: null,
      memoryRatio: null,
      fullestDiskRatio: null,
      runs: [],
      runsCount: 0,
      lastSeenAt: "2026-11-09T07:28:00Z",
    }),
    host("web-01", { runs: ["orders-api", "identity-api", "search-api"], runsCount: 3, cpuRatio: 0.18 }),
    host("web-02"),
    host("docker-01", { runs: ["6 containers"], cpuRatio: 0.27, memoryRatio: 0.55 }),
    host("orders-api-7f9", {
      kind: "container",
      os: null,
      parent: "docker-01",
      cpuRatio: 0.12,
      memoryRatio: 0.38,
      fullestDiskRatio: null,
    }),
  ],
  total: 7,
  next: null,
};

/** A database server, healthy unless said otherwise. */
function server(name: string, extra: Partial<DatabaseServerRow> = {}): DatabaseServerRow {
  return {
    engine: "microsoft.sql_server",
    server: name,
    version: "SQL Server 2022 CU14",
    health: "Ok",
    silent: false,
    databases: 8,
    sizeBytes: 96_000_000_000,
    growthBytesPerDay: 200_000_000,
    lastFullBackupAgeSeconds: 18_000,
    lateBackups: 0,
    blockingChains: 0,
    cpuRatio: 0.12,
    ...extra,
  };
}

/** The Databases list. */
export const databasesList: ResourcePage<DatabasesHeader, DatabaseServerRow> = {
  header: {
    servers: 4,
    silent: 1,
    databases: 26,
    dataBytes: 1_890_000_000_000,
    growthBytes: 13_000_000_000,
    lateBackups: 1,
    lateBackupFirst: { server: "SQL-PROD-01", database: "Reporting" },
    blockingChains: 3,
    blockingServer: "SQL-PROD-01",
  },
  items: [
    server("SQL-PROD-01", {
      health: "Caution",
      databases: 13,
      sizeBytes: 382_000_000_000,
      growthBytesPerDay: 1_800_000_000,
      lastFullBackupAgeSeconds: 21_600,
      lateBackups: 1,
      blockingChains: 3,
      cpuRatio: 0.28,
    }),
    server("pg-analytics", {
      engine: "postgresql",
      version: "PostgreSQL 16.6",
      silent: true,
      databases: 3,
      sizeBytes: null,
      growthBytesPerDay: null,
      lastFullBackupAgeSeconds: null,
      cpuRatio: null,
    }),
    server("SQL-APP-02", { version: "SQL Server 2019 CU28" }),
    server("pg-01", {
      engine: "postgresql",
      version: "PostgreSQL 17.2",
      databases: 6,
      sizeBytes: 212_000_000_000,
      lastFullBackupAgeSeconds: null,
      cpuRatio: 0.45,
    }),
  ],
  total: 4,
  next: null,
};
