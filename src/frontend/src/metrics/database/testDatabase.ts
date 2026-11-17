import type {
  PermissionsView,
  AgentJobView,
  AvailabilityView,
  ChartLine,
  ConfigurationView,
  DatabaseBlocking,
  DatabaseLive,
  DatabaseQueries,
  DatabaseQuery,
  DatabaseStorage,
  IndexAdvice,
  LiveSession,
  PlanWarning,
  QueryRow,
  ServerEvent,
  ServerView,
  StorageView,
  WaitTerm,
} from "../../api/types";

/** A line of 72 five-minute points ending at the fixtures' "now", gently wavy around `base`. */
function line(label: string, base: number, amplitude: number, seed = 1): ChartLine {
  const points = Array.from({ length: 72 }, (_, index) => ({
    at: new Date(Date.UTC(2026, 10, 10, 5, 5) + index * 300_000).toISOString(),
    value: Math.max(0, base + amplitude * Math.sin(index / 7 + seed)),
  }));

  return { label, latest: points.at(-1)?.value ?? null, points };
}

/** The statement of the monthly report that scans the Orders table. */
export const monthlyReportHash = "0x7A3C91D2E4F05B68";

/** The statement that updates Orders and is held up by the head blocker. */
export const updateOrdersHash = "0x11B0C7E94A2D3F85";

/** The lock wait, explained. */
const lockWait: WaitTerm = {
  type: "LCK_M_U",
  category: "Lock",
  simple: "waits for a lock",
  meaning: "The request waits for an update lock that another session holds.",
  causes: "A long transaction that has not committed.",
};

/** The disk wait, explained. */
const diskWait: WaitTerm = {
  type: "PAGEIOLATCH_SH",
  category: "Disk I/O",
  simple: "waits for the disk",
  meaning: "The request waits for a data page to be read from disk.",
  causes: "A scan of a large table, or a slow disk.",
};

/** The blocked session: waits on session 73 for a lock for two minutes. */
const blockedSession: LiveSession = {
  id: 117,
  status: "suspended",
  command: "UPDATE",
  database: "shop",
  program: "Shop.Api",
  host: "app-02",
  login: "app_shop",
  blockedBy: 73,
  blocking: 0,
  waitType: "LCK_M_U",
  waitSeconds: 134,
  waitResource: "KEY: 7:72057594043432960 (a1b2c3d4e5f6)",
  elapsedSeconds: 134,
  cpuSeconds: 0.02,
  reads: 12,
  writes: 0,
  tempdbBytes: 0,
  grantBytes: 0,
  grantRequestedBytes: 0,
  grantWaiting: false,
  transactionSeconds: 134,
  changedData: false,
  openTransactions: 1,
  isolation: "ReadCommitted",
  percentComplete: null,
  remainingSeconds: 0,
  queryHash: updateOrdersHash,
  planHash: null,
  idleSeconds: 0,
  connectedSeconds: 7_200,
  name: "Update the status of an order",
  text: "UPDATE dbo.Orders SET Status = @status WHERE CustomerId = @customerId AND Status = @old",
};

/** The head blocker: asleep with a transaction that changed data and was never committed. */
const headBlocker: LiveSession = {
  id: 73,
  status: "sleeping",
  command: "AWAITING COMMAND",
  database: "shop",
  program: "Shop.Admin",
  host: "admin-01",
  login: "app_admin",
  blockedBy: null,
  blocking: 1,
  waitType: null,
  waitSeconds: 0,
  waitResource: null,
  elapsedSeconds: 0,
  cpuSeconds: 0.4,
  reads: 40,
  writes: 18,
  tempdbBytes: 0,
  grantBytes: 0,
  grantRequestedBytes: 0,
  grantWaiting: false,
  transactionSeconds: 412,
  changedData: true,
  openTransactions: 1,
  isolation: "ReadCommitted",
  percentComplete: null,
  remainingSeconds: 0,
  queryHash: null,
  planHash: null,
  idleSeconds: 410,
  connectedSeconds: 3_600,
  name: "Update the orders of a customer",
  text: "UPDATE dbo.Orders SET Status = 'Hold' WHERE CustomerId = 4711",
};

/** The report that reads much of the reporting database, running now. */
const reportSession: LiveSession = {
  id: 203,
  status: "running",
  command: "SELECT",
  database: "reporting",
  program: "Reporting.Worker",
  host: "bi-01",
  login: "bi_reader",
  blockedBy: null,
  blocking: 0,
  waitType: "PAGEIOLATCH_SH",
  waitSeconds: 0.4,
  waitResource: null,
  elapsedSeconds: 41,
  cpuSeconds: 18,
  reads: 9_120_000,
  writes: 0,
  tempdbBytes: 2e9,
  grantBytes: 1e9,
  grantRequestedBytes: 1e9,
  grantWaiting: false,
  transactionSeconds: null,
  changedData: false,
  openTransactions: 0,
  isolation: "ReadCommitted",
  percentComplete: null,
  remainingSeconds: 0,
  queryHash: monthlyReportHash,
  planHash: "0xA1",
  idleSeconds: 0,
  connectedSeconds: 900,
  name: "Monthly sales by region",
  text: "EXEC dbo.MonthlyReport @month",
};

/** The Live tab: session 73 holds a lock that makes session 117 wait. */
export const liveView: DatabaseLive = {
  engine: "microsoft.sql_server",
  server: "sql-prod-01",
  environment: "production",
  sampledAt: "2026-11-10T11:00:08Z",
  ageSeconds: 4,
  at: null,
  tiles: {
    batchRequests: 1240,
    batchRequestsLine: line("batch requests", 1200, 140, 3),
    blocked: 1,
    blockedLine: line("blocked", 0.5, 0.5, 1),
    longestRequestSeconds: 134,
    oldestTransactionSeconds: 412,
    oldestTransactionSession: 73,
    grantsWaiting: 0,
    tempdbFreeRatio: 0.82,
    tempdbSizeBytes: 24e9,
  },
  findings: [
    {
      kind: "head-blocker",
      severity: "warning",
      title: "One connection holds up another",
      detail: "Shop.Admin on admin-01 changed data, then stopped answering for 410 s. Shop.Api waits for it.",
      advanced: "Session 73 sleeps with an open transaction and blocks session 117.",
      session: 73,
      actions: [
        { kind: "session", label: "Open connection 73", value: "73", hint: null },
        { kind: "logs", label: "See Shop.Admin's logs", value: "shop-admin", hint: null },
        { kind: "copy", label: "Copy the statement to end it", value: "KILL 73", hint: "A DBA runs it." },
      ],
    },
  ],
  blocking: [{ session: 73, waiting: 1, children: [{ session: 117, waiting: 0, children: [] }] }],
  sessions: [blockedSession, headBlocker, reportSession],
  waits: { LCK_M_U: lockWait, PAGEIOLATCH_SH: diskWait },
};

/** The Live tab when all is well: one report runs and nobody waits. */
export const liveQuietView: DatabaseLive = {
  ...liveView,
  tiles: { ...liveView.tiles, blocked: 0, oldestTransactionSeconds: null, oldestTransactionSession: null },
  findings: [],
  blocking: [],
  sessions: [reportSession],
};

/** A missing-index finding with the statement that creates it. */
const missingIndex: PlanWarning = {
  kind: "missing-index",
  severity: "caution",
  title: "An index would save reads",
  detail: "The plan reads the whole of dbo.Orders to find one customer's rows.",
  fix: "Create the index; a DBA reviews it first.",
  script: "CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId) INCLUDE (Status);",
};

/** An implicit conversion finding without a statement. */
const implicitConversion: PlanWarning = {
  kind: "implicit-conversion",
  severity: "warning",
  title: "A parameter has the wrong type",
  detail: "@month is converted for every row, so the index on Month cannot be used.",
  fix: "Declare @month with the column's type.",
  script: null,
};

/** The statement with a finding: the monthly report. */
export const monthlyReportRow: QueryRow = {
  queryHash: monthlyReportHash,
  database: "reporting",
  name: "Monthly sales by region",
  text: "EXEC dbo.MonthlyReport @month",
  totalSeconds: 80,
  executions: 12,
  cpuSeconds: 61,
  reads: 9_120_000,
  perRunSeconds: 6.7,
  plans: 2,
  findings: [implicitConversion],
};

/** The Queries tab: one statement with a finding and one without. */
export const queriesView: DatabaseQueries = {
  range: "6h",
  statements: [
    monthlyReportRow,
    {
      queryHash: updateOrdersHash,
      database: "shop",
      name: "Update the status of an order",
      text: "UPDATE dbo.Orders SET Status = @status WHERE CustomerId = @customerId AND Status = @old",
      totalSeconds: 12.5,
      executions: 400,
      cpuSeconds: 9.4,
      reads: 180_000,
      perRunSeconds: 0.03,
      plans: 1,
      findings: [],
    },
  ],
  agentStatements: 6,
  agentSeconds: 1.2,
  queryStore: [
    { database: "shop", on: true, script: null },
    { database: "reporting", on: false, script: "ALTER DATABASE [reporting] SET QUERY_STORE = ON;" },
  ],
};

/** The monthly report's own page. */
export const queryView: DatabaseQuery = {
  queryHash: monthlyReportHash,
  database: "reporting",
  name: "Monthly sales by region",
  text: "EXEC dbo.MonthlyReport @month",
  range: "6h",
  totals: monthlyReportRow,
  points: Array.from({ length: 12 }, (_, index) => ({
    start: new Date(Date.UTC(2026, 10, 10, 5, 0) + index * 1_800_000).toISOString(),
    runs: 1,
    perRunSeconds: 6 + (index % 3),
    cpuPerRunSeconds: 5,
    readsPerRun: 760_000,
    plan: "0xA1",
  })),
  plans: [
    {
      hash: "0xA1",
      runs: 12,
      perRunSeconds: 6.7,
      cpuPerRunSeconds: 5.1,
      readsPerRun: 760_000,
      firstSeen: "2026-11-09T08:00:00Z",
      lastSeen: "2026-11-10T10:30:00Z",
      captured: true,
      operators: [
        {
          depth: 0,
          physical: "Clustered Index Scan",
          logical: "Clustered Index Scan",
          target: "dbo.Orders",
          costShare: 0.82,
          estimatedRows: 1_200_000,
          warned: true,
        },
      ],
      warnings: [implicitConversion],
    },
  ],
  findings: [implicitConversion],
  callers: [{ program: "Reporting.Worker", host: "bi-01", login: "bi_reader", samples: 40 }],
  history: [],
  waits: [{ category: "Disk I/O", seconds: 21 }],
};

/** The Blocking tab: one episode behind session 73 and one deadlock. */
export const blockingView: DatabaseBlocking = {
  range: "6h",
  episodes: [
    {
      start: "2026-11-10T10:53:00Z",
      end: "2026-11-10T11:00:00Z",
      head: 73,
      program: "Shop.Admin",
      host: "admin-01",
      sleeping: true,
      headName: "Update the orders of a customer",
      headText: "UPDATE dbo.Orders SET Status = 'Hold' WHERE CustomerId = 4711",
      mostWaiting: 3,
      longestWaitSeconds: 134,
      waitingPrograms: ["Shop.Api"],
      summary: "Shop.Admin on admin-01 held a lock while it slept; Shop.Api waited up to 2 min.",
    },
  ],
  deadlocks: [
    {
      key: "dl-1",
      at: "2026-11-10T09:12:00Z",
      summary: "Two updates of Orders and Customers waited for each other.",
      processes: [
        {
          id: "p1",
          session: 90,
          program: "Shop.Api",
          host: "app-01",
          login: "app_shop",
          database: "shop",
          victim: true,
          waits: "LCK_M_U",
          name: "Update an order",
          text: "UPDATE dbo.Orders SET Status = 'Paid' WHERE Id = 1",
        },
        {
          id: "p2",
          session: 91,
          program: "Shop.Api",
          host: "app-02",
          login: "app_shop",
          database: "shop",
          victim: false,
          waits: "LCK_M_U",
          name: "Update a customer",
          text: "UPDATE dbo.Customers SET Balance = 0 WHERE Id = 2",
        },
      ],
      resources: [{ kind: "KEY", target: "dbo.Orders.PK_Orders", holders: ["p2"], waiters: ["p1"] }],
    },
  ],
};

/** One database of the storage read, healthy unless overridden. */
function storageOf(name: string, overrides: Partial<DatabaseStorage> = {}): DatabaseStorage {
  return {
    name,
    state: "ONLINE",
    recoveryModel: "FULL",
    compatibility: "160",
    pageVerify: "CHECKSUM",
    autoClose: false,
    autoShrink: false,
    readCommittedSnapshot: true,
    encrypted: false,
    logReuseWait: "NOTHING",
    logUsedRatio: 0.12,
    virtualLogFiles: 24,
    files: [
      { name: `${name}_data`, type: "Rows", volume: "D:", sizeBytes: 412e9, growth: "1 GB", maxSize: "unlimited" },
      { name: `${name}_log`, type: "Log", volume: "L:", sizeBytes: 12e9, growth: "512 MB", maxSize: "unlimited" },
    ],
    fullBackupAgeSeconds: 7 * 3600,
    diffBackupAgeSeconds: 3600,
    logBackupAgeSeconds: 600,
    logChainBroken: false,
    lastFullSeconds: 1_800,
    lastFullBytes: 80e9,
    lastFullDestination: `\\\\backup-01\\sql\\${name}.bak`,
    checkdbAgeSeconds: 2 * 86_400,
    damagedPages: 0,
    findings: [],
    ...overrides,
  };
}

/** The Databases tab's storage: shop has no full backup for a day, with the statement to take one. */
export const storageView: StorageView = {
  databases: [
    storageOf("shop", {
      fullBackupAgeSeconds: 31 * 3600,
      findings: [
        {
          kind: "backup-late",
          severity: "warning",
          title: "shop has no full backup for 1 d 7 h",
          detail: "The nightly backup job failed.",
          fix: "Run a full backup now.",
          script: "BACKUP DATABASE [shop] TO DISK = N'\\\\backup-01\\sql\\shop.bak' WITH CHECKSUM;",
        },
      ],
    }),
    storageOf("reporting", { fullBackupAgeSeconds: null, lastFullSeconds: null, lastFullBytes: null }),
    storageOf("tempdb", { recoveryModel: "SIMPLE", logBackupAgeSeconds: null, lastFullDestination: null }),
  ],
  volumes: [
    { volume: "D:", sizeBytes: 2e12, freeBytes: 0.3e12, usedRatio: 0.85, daysUntilFull: 41 },
    { volume: "L:", sizeBytes: 200e9, freeBytes: 150e9, usedRatio: 0.25, daysUntilFull: null },
  ],
  findings: [],
};

/** The index advice: an unused index and a missing one with its statement. */
export const indexesView: IndexAdvice = {
  findings: [missingIndex],
  indexes: [
    {
      database: "shop",
      table: "dbo.Orders",
      name: "IX_Orders_Status",
      type: "NONCLUSTERED",
      keys: "Status",
      include: "",
      sizeBytes: 3e9,
      reads: 0,
      writes: 52_000,
      escalations: 0,
    },
  ],
};

/** The Server tab: CPU, memory, workload, and the wait types. */
export const serverView: ServerView = {
  version: "SQL Server 2022",
  updateLevel: "CU 14",
  edition: "Enterprise",
  uptimeSeconds: 41 * 86_400,
  unsupported: false,
  sqlCpu: 0.34,
  otherCpu: 0.04,
  runnable: 1,
  workers: 120,
  memoryTarget: 48e9,
  memoryCommitted: 47e9,
  osMemoryFree: 4e9,
  osMemoryLow: false,
  grantsWaiting: 0,
  dumpsDay: 0,
  agentStopped: false,
  requests: 1240,
  requestsUsual: 1180,
  sqlCpuLine: line("SQL Server CPU", 0.34, 0.08, 2),
  otherCpuLine: line("Other CPU", 0.04, 0.01, 3),
  waits: [
    { term: lockWait, seconds: 440 },
    { term: diskWait, seconds: 120 },
  ],
  findings: [],
};

/** The Jobs tab: the nightly backup failed, the index maintenance ran. */
export const jobsView: AgentJobView[] = [
  {
    name: "Nightly full backup - shop",
    failed: true,
    lastRun: "2026-11-10T01:00:00Z",
    lastSeconds: 4_324,
    runningSeconds: null,
    nextRun: "2026-11-11T01:00:00Z",
    late: false,
    usualSeconds: 1_800,
    runs: [
      {
        at: "2026-11-10T01:00:00Z",
        succeeded: false,
        seconds: 4_324,
        message: "The job failed.",
        steps: [
          {
            id: 1,
            name: "Back up shop",
            succeeded: false,
            seconds: 4_324,
            message: "Operating system error 112: disk full.",
          },
        ],
      },
    ],
  },
  {
    name: "Index maintenance",
    failed: false,
    lastRun: "2026-11-10T00:00:00Z",
    lastSeconds: 1_341,
    runningSeconds: null,
    nextRun: "2026-11-11T00:00:00Z",
    late: false,
    usualSeconds: 1_300,
    runs: [],
  },
];

/** The Events tab: a deadlock the server reported. */
export const eventsView: ServerEvent[] = [
  {
    at: "2026-11-10T09:12:00Z",
    name: "xml_deadlock_report",
    severity: "warning",
    words: "A deadlock happened",
    error: 1205,
    errorSeverity: 13,
    wait: "",
    message: "Transaction was deadlocked on lock resources and was chosen as the deadlock victim.",
  },
];

/** The availability read: one group with a healthy secondary. */
export const availabilityView: AvailabilityView = {
  replicas: [
    { group: "ag-shop", replica: "sql-prod-01", role: "PRIMARY", healthy: true },
    { group: "ag-shop", replica: "sql-prod-02", role: "SECONDARY", healthy: true },
  ],
  databases: [
    { group: "ag-shop", replica: "sql-prod-02", database: "shop", queueBytes: 2e6, lagSeconds: 1, synchronizing: true },
  ],
  logShipping: [],
  nodes: [{ node: "sql-prod-01", state: "Up", owner: true }],
};

/** The configuration read: a setting off its recommendation. */
export const configurationView: ConfigurationView = {
  settings: [{ name: "max degree of parallelism", value: "0", recommended: "8" }],
  findings: [],
  sysadmins: ["sa", "VEYRA\\dba"],
  changes: [],
};

/** A login that may read everything (VEY-58). */
export const permissionsGranted: PermissionsView = {
  reported: true,
  login: "veyra_monitor",
  platform: "sqlserver",
  gaps: [],
  script: null,
};

/** An Azure SQL Database whose contained user may read everything (VEY-57). */
export const permissionsAzureDatabase: PermissionsView = { ...permissionsGranted, platform: "azure-sql-database" };

/** A Managed Instance whose login may read everything (VEY-57). */
export const permissionsManagedInstance: PermissionsView = { ...permissionsGranted, platform: "managed-instance" };

/** A login created before the job steps and schedules were read (VEY-58). */
export const permissionsMissingJobSteps: PermissionsView = {
  reported: true,
  login: "veyra_monitor",
  platform: "sqlserver",
  gaps: [{ permission: "SELECT ON msdb.dbo.sysjobsteps", tabs: ["jobs"], reads: "the steps of each job run" }],
  script:
    "USE msdb;\nIF USER_ID(N'veyra_monitor') IS NULL CREATE USER [veyra_monitor] FOR LOGIN [veyra_monitor];\n" +
    "GRANT SELECT ON dbo.sysjobsteps TO [veyra_monitor];\nGO",
};
