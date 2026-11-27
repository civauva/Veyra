import type { ServerBackups, ServerOverview } from "../../api/types";

/*
 * A SQL Server's overview and backups as the server answers them (ADR 0025), shaped after reference screenshots 1, 3,
 * and 8, for the component and end-to-end tests of the Console SQL Server screen (VEY-75).
 */

const to = "2026-11-26T14:00:00.000Z";
const from = "2026-11-25T14:00:00.000Z";

/** A wavy line of so many points around a value. */
function wave(count: number, around: number, swing: number): number[] {
  return Array.from({ length: count }, (_, index) => around + Math.sin(index / 3) * swing);
}

/** The overview of SQL-PROD-01, a busy server with three blocking chains and a filling volume. */
export const serverOverview: ServerOverview = {
  from,
  to,
  machine: {
    version: "16.0.4135.4",
    updateLevel: "CU14",
    edition: "Enterprise Edition",
    osName: "Windows Server 2022",
    osRelease: "21H2",
    cpus: 16,
    memoryBytes: 64 * 1024 ** 3,
    startedAt: "2026-11-09T10:00:00.000Z",
    uptimeSeconds: 17 * 86_400 + 4 * 3600,
    databases: 13,
    platform: "box",
  },
  figures: {
    cpu: { value: 0.28, line: wave(24, 0.3, 0.1), change: -0.12 },
    sqlMemory: { value: 36.4 * 1024 ** 3, line: wave(24, 36 * 1024 ** 3, 1024 ** 3), change: 0.02 },
    sessions: { value: 198, line: wave(24, 190, 20), change: -0.14 },
    blocked: { value: 11, line: wave(24, 3, 3), change: 2.6 },
    size: { value: 382.6 * 1024 ** 3, line: wave(24, 380 * 1024 ** 3, 1024 ** 3), change: 0.03 },
    lastFullBackupSeconds: { value: 6 * 3600 + 12 * 60, line: [], change: null },
    batchRequests: { value: 1842, line: wave(24, 1800, 200), change: 0.062 },
    volumeFree: { value: 0.11, line: wave(24, 0.14, 0.02), change: -0.04, volume: "D:" },
  },
  performance: {
    bucketSeconds: 3600,
    starts: Array.from({ length: 24 }, (_, index) => new Date(Date.parse(from) + index * 3_600_000).toISOString()),
    cpu: wave(24, 0.3, 0.12),
    sqlMemoryRatio: wave(24, 0.57, 0.02),
    waitMsPerSecond: wave(24, 14, 6),
  },
  counters: [
    { name: "batchRequests", value: 1842, unit: "per second", change: 0.062 },
    { name: "compilations", value: 38, unit: "per second", change: -0.174 },
    { name: "pageLife", value: 4912, unit: "seconds", change: 0.226 },
    { name: "waits", value: 2304, unit: "ms per second", change: 0.153 },
  ],
  io: {
    readBytesPerSecond: 142 * 1024 ** 2,
    writeBytesPerSecond: 39 * 1024 ** 2,
    readLine: wave(24, 140 * 1024 ** 2, 30 * 1024 ** 2),
    writeLine: wave(24, 40 * 1024 ** 2, 10 * 1024 ** 2),
  },
  waits: {
    totalSeconds: 42.6 * 60,
    types: [
      { type: "LCK_M_X", category: "Lock", seconds: 16.3 * 60, share: 0.384 },
      { type: "CXPACKET", category: "Parallelism", seconds: 7.9 * 60, share: 0.185 },
      { type: "PAGEIOLATCH_SH", category: "Buffer IO", seconds: 6.1 * 60, share: 0.143 },
      { type: "SOS_SCHEDULER_YIELD", category: "CPU", seconds: 5.2 * 60, share: 0.122 },
      { type: "Other", category: null, seconds: 7.1 * 60, share: 0.166 },
    ],
  },
  storage: {
    totalBytes: 382.6 * 1024 ** 3,
    growth24hBytes: 12.4 * 1024 ** 3,
    growth7dBytes: 38.1 * 1024 ** 3,
    growth30dBytes: 142 * 1024 ** 3,
    line: Array.from({ length: 30 }, (_, index) => (240 + index * 4.7) * 1024 ** 3),
    largest: [
      {
        database: "SalesDB",
        bytes: 146.2 * 1024 ** 3,
        growth24hBytes: 4.8 * 1024 ** 3,
        growth7dBytes: 5.4 * 1024 ** 3,
      },
      { database: "OrderDB", bytes: 78.6 * 1024 ** 3, growth24hBytes: 2.9 * 1024 ** 3, growth7dBytes: 1.8 * 1024 ** 3 },
      {
        database: "WarehouseDB",
        bytes: 52.1 * 1024 ** 3,
        growth24hBytes: 2.1 * 1024 ** 3,
        growth7dBytes: 0.9 * 1024 ** 3,
      },
    ],
    volumes: [
      { name: "C:", sizeBytes: 476 * 1024 ** 3, freeBytes: 178 * 1024 ** 3, freeRatio: 0.37, daysUntilFull: null },
      { name: "D:", sizeBytes: 2 * 1024 ** 4, freeBytes: 223 * 1024 ** 3, freeRatio: 0.11, daysUntilFull: 1.6 },
      { name: "E:", sizeBytes: 2 * 1024 ** 4, freeBytes: 1.42 * 1024 ** 4, freeRatio: 0.71, daysUntilFull: null },
    ],
  },
  inventory: [
    {
      database: "SalesDB",
      dataBytes: 146 * 1024 ** 3,
      logBytes: 24 * 1024 ** 3,
      growthPerDayBytes: 0.7 * 1024 ** 3,
      state: "ONLINE",
      recoveryModel: "FULL",
    },
    {
      database: "OrderDB",
      dataBytes: 58.7 * 1024 ** 3,
      logBytes: 12.1 * 1024 ** 3,
      growthPerDayBytes: 0.41 * 1024 ** 3,
      state: "ONLINE",
      recoveryModel: "FULL",
    },
    {
      database: "Archive",
      dataBytes: 5 * 1024 ** 3,
      logBytes: 1.1 * 1024 ** 3,
      growthPerDayBytes: 0,
      state: "OFFLINE",
      recoveryModel: "SIMPLE",
    },
  ],
  blocking: {
    chains: [
      {
        headSessionId: 64,
        database: "SalesDB",
        login: "sa",
        host: "APP-01",
        waitType: "LCK_M_X",
        waitSeconds: 42 * 60 + 19,
        blockedCount: 7,
        statement: "UPDATE dbo.Orders SET Status = 'Processing' WHERE OrderId = @OrderId;",
        blocked: [
          { sessionId: 77, waitType: "LCK_M_X", waitSeconds: 765 },
          { sessionId: 81, waitType: "LCK_M_X", waitSeconds: 763 },
        ],
      },
      {
        headSessionId: 115,
        database: "OrderDB",
        login: "app_user",
        host: "APP-02",
        waitType: "LCK_M_IX",
        waitSeconds: 201,
        blockedCount: 3,
        statement: "DELETE FROM dbo.OrderItems WHERE OrderId = @OrderId;",
        blocked: [{ sessionId: 120, waitType: "LCK_M_IX", waitSeconds: 201 }],
      },
    ],
    blockedLine: wave(24, 3, 3),
    deadlocks: 0,
  },
  coverage: [
    { area: "activity", delivering: true, lastSeen: to, missingPermission: null },
    { area: "waits", delivering: true, lastSeen: to, missingPermission: null },
    { area: "queries", delivering: true, lastSeen: to, missingPermission: null },
    { area: "storage", delivering: true, lastSeen: to, missingPermission: null },
    { area: "backups", delivering: true, lastSeen: to, missingPermission: null },
    { area: "jobs", delivering: false, lastSeen: null, missingPermission: "SQLAgentReaderRole in msdb" },
  ],
};

const days = Array.from({ length: 7 }, (_, index) =>
  new Date(Date.parse(to) - (6 - index) * 86_400_000).toISOString().slice(0, 10),
);

/** The backups of SQL-PROD-01: one database overdue, logs every 15 minutes. */
export const serverBackups: ServerBackups = {
  from,
  to,
  selfManaged: false,
  backups: [
    {
      database: "SalesDB",
      type: "log",
      startedAt: "2026-11-26T13:59:00.000Z",
      finishedAt: "2026-11-26T13:59:04.000Z",
      seconds: 4,
      bytes: 212 * 1024 ** 2,
      compressedBytes: 80 * 1024 ** 2,
      copyOnly: false,
      device: "E:\\Backups\\SalesDB_log.trn",
    },
    {
      database: "SalesDB",
      type: "full",
      startedAt: "2026-11-26T07:31:00.000Z",
      finishedAt: "2026-11-26T07:48:00.000Z",
      seconds: 1020,
      bytes: 41.2 * 1024 ** 3,
      compressedBytes: 14 * 1024 ** 3,
      copyOnly: false,
      device: "E:\\Backups\\SalesDB.bak",
    },
    {
      database: "SalesDB",
      type: "differential",
      startedAt: "2026-11-26T02:00:00.000Z",
      finishedAt: "2026-11-26T02:03:00.000Z",
      seconds: 180,
      bytes: 6.1 * 1024 ** 3,
      compressedBytes: null,
      copyOnly: false,
      device: null,
    },
  ],
  days: days.map((day, index) => ({
    day,
    full: { count: 12, seconds: (14 + index) * 60 },
    differential: { count: index % 2, seconds: index % 2 ? 180 : 0 },
    log: { count: 96, seconds: 400 },
  })),
  grid: {
    days,
    databases: [
      { database: "SalesDB", cells: days.map((_, index) => ({ full: true, differential: index % 2 === 1, log: 96 })) },
      { database: "ReportingDB", cells: days.map((_, index) => ({ full: index < 5, differential: false, log: 96 })) },
    ],
  },
  freshness: [
    {
      database: "ReportingDB",
      fullAgeSeconds: 31 * 3600,
      diffAgeSeconds: 5 * 3600,
      logAgeSeconds: 360,
      recoveryModel: "FULL",
      state: "warning",
      policy: "Full backup within 26 h, warning after 50 h",
    },
    {
      database: "SalesDB",
      fullAgeSeconds: 6 * 3600,
      diffAgeSeconds: 3600,
      logAgeSeconds: 120,
      recoveryModel: "FULL",
      state: "ok",
      policy: "Full backup within 26 h, warning after 50 h",
    },
    {
      database: "Archive",
      fullAgeSeconds: 6 * 86_400,
      diffAgeSeconds: null,
      logAgeSeconds: null,
      recoveryModel: "SIMPLE",
      state: "ignored",
      policy: "Full backup within 26 h, warning after 50 h",
    },
  ],
};
