import type {
  Agent,
  IntegrationDetails,
  IntegrationItem,
  IntegrationList,
  IntegrationModule,
  IntegrationModuleInfo,
  Probe,
  ProbeResult,
} from "../api/types";
import { builtInAgent } from "../settings/testMonitoring";

/*
 * Integrations as the server answers them (ADR 0028), after the VEY-78 mockups, for the component and end-to-end tests
 * of the integration screens: three SQL Servers, the template's modules, probes, and sql-prod-01's package page.
 */

/** An agent installed on a Windows server in the database network, which may sign in with its own account. */
export const windowsAgent: Agent = {
  ...builtInAgent,
  id: "a2",
  name: "sql-prod agent",
  placement: "Remote",
  hostName: "SQLPROD-AGENT",
  operatingSystem: "Windows Server 2022 Datacenter",
  servers: 1,
};

/** The agents of the wizard: the built-in one on Linux and the Windows one. */
export const wizardAgents: Agent[] = [builtInAgent, windowsAgent];

/** The SQL Server template's modules, in the catalog's order. */
export const moduleCatalog: IntegrationModuleInfo[] = [
  {
    id: "core",
    name: "Core",
    description: "Performance and health: CPU, memory, waits, sessions, counters",
    defaultIntervalSeconds: 15,
    choices: [10, 15, 30, 60],
    required: true,
  },
  {
    id: "activity",
    name: "Activity and blocking",
    description: "Live sessions, blocking chains, deadlocks",
    defaultIntervalSeconds: 10,
    choices: [5, 10, 15, 30, 60],
    required: false,
  },
  {
    id: "queries",
    name: "Queries",
    description: "Top statements, plans, regressions, Query Store",
    defaultIntervalSeconds: 900,
    choices: [300, 600, 900, 1800, 3600],
    required: false,
  },
  {
    id: "storage",
    name: "Storage",
    description: "Files, sizes, growth, volumes",
    defaultIntervalSeconds: 300,
    choices: [60, 300, 600, 900, 1800],
    required: false,
  },
  {
    id: "backups",
    name: "Backups",
    description: "Full, differential and log history, freshness",
    defaultIntervalSeconds: 300,
    choices: [60, 300, 600, 900, 1800],
    required: false,
  },
  {
    id: "jobs",
    name: "Jobs",
    description: "SQL Server Agent runs and steps",
    defaultIntervalSeconds: 60,
    choices: [30, 60, 300, 600],
    required: false,
  },
  {
    id: "events",
    name: "Events",
    description: "system_health errors and deadlock graphs",
    defaultIntervalSeconds: 60,
    choices: [30, 60, 300],
    required: false,
  },
  {
    id: "configuration",
    name: "Configuration",
    description: "Server and database settings, and their changes",
    defaultIntervalSeconds: 3600,
    choices: [900, 3600, 21_600, 86_400],
    required: false,
  },
  {
    id: "indexes",
    name: "Indexes",
    description: "Missing, unused, and fragmented indexes, and statistics",
    defaultIntervalSeconds: 21_600,
    choices: [3600, 21_600, 86_400],
    required: false,
  },
];

/** The modules of sql-prod-01: Events, Configuration, and Indexes off. */
const prodModules: IntegrationModule[] = moduleCatalog.map((module) => ({
  id: module.id,
  enabled: !["events", "configuration", "indexes"].includes(module.id),
  intervalSeconds: module.defaultIntervalSeconds,
}));

/** sql-prod-01, read by the Windows agent on the current template. */
export const prodIntegration: IntegrationItem = {
  id: "s1",
  kind: "sqlserver",
  name: "sql-prod-01",
  environment: "production",
  agent: { id: "a2", name: "sql-prod agent" },
  version: "0.2.0",
  latestVersion: "0.2.0",
  state: "Receiving",
  modules: prodModules,
  createdAt: "2026-11-13T08:00:00Z",
  changedAt: "2026-11-25T22:14:00Z",
};

/** sql-report-02, on the previous template, whose login fails. */
export const reportIntegration: IntegrationItem = {
  id: "s2",
  kind: "sqlserver",
  name: "sql-report-02",
  environment: "production",
  agent: { id: "a1", name: "Built in" },
  version: "0.1.0",
  latestVersion: "0.2.0",
  state: "LoginFailed",
  modules: moduleCatalog.map((module) => ({
    id: module.id,
    enabled: true,
    intervalSeconds: module.defaultIntervalSeconds,
  })),
  createdAt: "2026-11-13T09:00:00Z",
  changedAt: "2026-11-13T09:00:00Z",
};

/** sql-branch-03, just added, waiting for its first figures. */
export const branchIntegration: IntegrationItem = {
  id: "s3",
  kind: "sqlserver",
  name: "sql-branch-03",
  environment: "branch",
  agent: { id: "a1", name: "Built in" },
  version: "0.2.0",
  latestVersion: "0.2.0",
  state: "WaitingForData",
  modules: prodModules,
  createdAt: "2026-11-26T11:59:20Z",
  changedAt: "2026-11-26T11:59:20Z",
};

/** The integrations list. */
export const integrationList: IntegrationList = {
  items: [prodIntegration, reportIntegration, branchIntegration],
};

/** A probe the agent has not answered yet. */
export const pendingProbe: Probe = {
  id: "p-1",
  state: "pending",
  startedAt: "2026-11-26T14:02:00Z",
  finishedAt: null,
  error: null,
  result: null,
};

/** What a probe read of sql-prod-01: SQL Server 2022 CU14, Enterprise, 96 databases, nothing missing. */
export const probeResult: ProbeResult = {
  version: "16.0.4135.4",
  updateLevel: "CU14",
  edition: "Enterprise Edition (64-bit)",
  platform: "Windows",
  databases: 96,
  databaseNames: ["master", "model", "msdb", "tempdb", "orders", "billing", "reporting"],
  missingPermissions: [],
};

/** A probe that read sql-prod-01. */
export const succeededProbe: Probe = {
  ...pendingProbe,
  state: "succeeded",
  finishedAt: "2026-11-26T14:02:41Z",
  result: probeResult,
};

/** A probe whose login SQL Server refused. */
export const failedProbe: Probe = {
  ...pendingProbe,
  state: "failed",
  finishedAt: "2026-11-26T14:02:30Z",
  error: { reason: "login-failed", message: "Login failed for user 'veyra_monitor'." },
};

/** A wavy line of so many points around a value. */
function wave(count: number, around: number, swing: number, phase = 0): number[] {
  return Array.from({ length: count }, (_, index) => Math.round(around + Math.sin(index / 2.4 + phase) * swing));
}

/** The figures of a module over the window: switched-off ones wrote nothing. */
function moduleFigures(module: IntegrationModuleInfo, series: number, index: number): IntegrationDetails["modules"][0] {
  const setting = prodModules.find((one) => one.id === module.id);
  const enabled = setting?.enabled ?? false;
  const perHour = enabled ? Math.round((3600 / module.defaultIntervalSeconds) * series) : 0;

  return {
    id: module.id,
    name: module.name,
    enabled,
    intervalSeconds: setting?.intervalSeconds ?? module.defaultIntervalSeconds,
    series: enabled ? series : 0,
    points: perHour * 24,
    line: enabled ? wave(24, perHour, perHour * 0.08, index) : [],
  };
}

/** sql-prod-01's package page, after the VEY-78 mockups. */
export const prodDetails: IntegrationDetails = {
  integration: prodIntegration,
  package: {
    name: "veyra-sqlserver-integration",
    version: "0.2.0",
    latestVersion: "0.2.0",
    previousVersion: "0.1.0",
    setAt: "2026-11-25T22:14:00Z",
    setBy: "Dana K.",
    collectorVersion: "0.140.0",
    collectorCompatibility: "otelcol-contrib ≥ 0.140.0",
    minimumSqlServer: "SQL Server 2016",
    login: "veyra_monitor",
    authentication: "sql",
    loginLacks: [],
  },
  collector: {
    agentId: "a2",
    agentName: "sql-prod agent",
    healthy: true,
    startedAt: "2026-11-23T00:14:00Z",
    uptimeSeconds: 3 * 86_400 + 14 * 3600,
    lastSeenAt: "2026-11-26T14:00:00Z",
    configState: "applied",
    configError: null,
  },
  validation: {
    lastProbe: { ...succeededProbe, startedAt: "2026-11-25T22:12:30Z", finishedAt: "2026-11-25T22:13:00Z" },
  },
  modules: moduleCatalog.map((module, index) =>
    moduleFigures(module, [38, 8, 14, 16, 12, 6, 4, 22, 9][index] ?? 4, index),
  ),
  checks: [
    { name: "server", module: "Core", secondsPerRun: 0.063, runs: 5760, ok: true },
    { name: "main counters", module: "Core", secondsPerRun: 0.142, runs: 5760, ok: true },
    { name: "live sessions", module: "Activity", secondsPerRun: 0.038, runs: 8640, ok: true },
    { name: "backups", module: "Backups", secondsPerRun: 0.198, runs: 288, ok: true },
    { name: "storage", module: "Storage", secondsPerRun: 0.176, runs: 288, ok: true },
    { name: "permissions", module: "Core", secondsPerRun: 0.012, runs: 96, ok: true },
  ],
  nextSteps: [
    {
      id: "thresholds",
      text: "Set alert thresholds for backups, blocking, storage",
      done: false,
      link: "/settings?section=Alerts",
    },
    { id: "channel", text: "Add a notification channel", done: true, link: "/notifications" },
    { id: "retention", text: "Review retention and sampling", done: false, link: "/settings?section=Retention" },
  ],
  history: [
    { at: "2026-11-25T22:14:00Z", by: "Dana K.", kind: "version", from: "0.1.0", to: "0.2.0" },
    { at: "2026-11-20T09:30:00Z", by: "Ana Admin", kind: "modules", from: null, to: null },
    { at: "2026-11-13T08:00:00Z", by: "Ana Admin", kind: "created", from: null, to: "0.1.0" },
  ],
};
