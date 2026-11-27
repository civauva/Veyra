import type {
  Agent,
  CollectorConfigState,
  IntegrationHistoryItem,
  IntegrationItem,
  IntegrationModule,
  ProbeFailure,
  ProbeResult,
  SqlServerAuthentication,
} from "../api/types";
import { loginScript } from "../settings/monitoringFormat";

/*
 * The words and links every look's integration screens share (ADR 0028). They only word what the server sent; the
 * server stores the modules, runs the probes, and works out the package page's figures.
 */

/** How each way of signing in is offered in the connection's select. */
export const authenticationLabels: Record<SqlServerAuthentication, string> = {
  sql: "SQL Server authentication",
  ntlm: "Windows authentication (NTLM)",
  integrated: "Windows authentication (integrated)",
};

/** How each way of signing in is named in a summary. */
export const authenticationShort: Record<SqlServerAuthentication, string> = {
  sql: "SQL Server",
  ntlm: "Windows (NTLM)",
  integrated: "Windows (integrated)",
};

/**
 * Whether an agent runs on Windows, so it may sign in with its own service account (ADR 0028).
 *
 * @param agent - The agent, or undefined.
 * @returns True when its operating system says Windows.
 */
export function isWindowsAgent(agent: Agent | undefined): boolean {
  return /windows/i.test(agent?.operatingSystem ?? "");
}

/**
 * An agent as the select and the summary name it: the built-in one, or its name with its system.
 *
 * @param agent - The agent.
 * @returns Such as "Built-in agent" or "sql-prod agent (Windows)".
 */
export function agentWords(agent: Agent): string {
  if (agent.placement === "BuiltIn") {
    return "Built-in agent, beside Veyra";
  }

  const system = isWindowsAgent(agent) ? "Windows" : agent.operatingSystem ? "Linux" : null;

  return system ? `${agent.name} (${system})` : agent.name;
}

/**
 * An agent named in a sentence: "the sql-prod agent" when its name says agent, "the agent Built in" otherwise.
 *
 * @param name - The agent's name.
 * @returns The words.
 */
export function readerWords(name: string): string {
  return /(^|\s)agent$/i.test(name.trim()) ? `the ${name.trim()}` : `the agent ${name.trim()}`;
}

/**
 * Where the agent connects: `host\INSTANCE` for a named instance, `host:port` otherwise.
 *
 * @param address - The host.
 * @param instance - The named instance, or null.
 * @param port - The port.
 * @returns The target.
 */
export function targetWords(address: string, instance: string | null, port: number | string): string {
  const host = address.trim() || "the server";

  return instance ? `${host}\\${instance}` : `${host}:${port}`;
}

/**
 * An interval in the largest unit that fits: `10 s`, `1 min`, `15 min`, `1 h`, `6 h`, `1 d`.
 *
 * @param seconds - The interval.
 * @returns The words.
 */
export function intervalWords(seconds: number): string {
  if (seconds < 60) {
    return `${seconds} s`;
  }

  if (seconds < 3600) {
    const rest = seconds % 60;

    return rest === 0 ? `${seconds / 60} min` : `${Math.floor(seconds / 60)} min ${rest} s`;
  }

  if (seconds < 86_400) {
    const rest = seconds % 3600;

    return rest === 0 ? `${seconds / 3600} h` : `${Math.floor(seconds / 3600)} h ${Math.round(rest / 60)} min`;
  }

  return `${Math.round(seconds / 86_400)} d`;
}

/**
 * How often the switched-on modules are read, from the shortest interval to the longest: `10 s – 15 min`.
 *
 * @param modules - The modules.
 * @returns The span, one interval, or a dash when none is on.
 */
export function collectionWords(modules: IntegrationModule[]): string {
  const intervals = modules.filter((module) => module.enabled).map((module) => module.intervalSeconds);

  if (intervals.length === 0) {
    return "—";
  }

  const shortest = Math.min(...intervals);
  const longest = Math.max(...intervals);

  return shortest === longest
    ? `every ${intervalWords(shortest)}`
    : `${intervalWords(shortest)} – ${intervalWords(longest)}`;
}

/**
 * How many modules are on: `6 of 9`.
 *
 * @param modules - The modules.
 * @returns The words.
 */
export function modulesWords(modules: IntegrationModule[]): string {
  return `${modules.filter((module) => module.enabled).length} of ${modules.length}`;
}

/** A version against the current one, as a list or a package page says it. */
export interface VersionWords {
  /** The words, such as "0.2.0 · current" or "0.1.0 · 0.2.0 available". */
  text: string;
  /** Whether a newer version is there to update to. */
  behind: boolean;
}

/**
 * An integration's template version against the current one.
 *
 * @param item - The integration.
 * @returns The words and whether it is behind.
 */
export function versionWords(item: Pick<IntegrationItem, "version" | "latestVersion">): VersionWords {
  return item.version === item.latestVersion
    ? { text: `${item.version} · current`, behind: false }
    : { text: `${item.version} · ${item.latestVersion} available`, behind: true };
}

/** The release year of each major version of SQL Server. */
const releaseYears: Record<string, string> = {
  "11": "2012",
  "12": "2014",
  "13": "2016",
  "14": "2017",
  "15": "2019",
  "16": "2022",
  "17": "2025",
};

/**
 * The product as people name it: `SQL Server 2022 CU14 (16.0.4135.4)`.
 *
 * @param version - The version number SQL Server reports.
 * @param updateLevel - The cumulative update, or null.
 * @returns The name.
 */
export function productName(version: string, updateLevel: string | null): string {
  const year = releaseYears[version.split(".")[0] ?? ""];

  return [`SQL Server${year ? ` ${year}` : ""}`, updateLevel, `(${version})`].filter(Boolean).join(" ");
}

/**
 * What a successful probe read, as one line of facts: product, edition, databases, and permissions.
 *
 * @param result - The probe's result.
 * @returns Such as "SQL Server 2022 CU14 (16.0.4135.4) · Enterprise · 96 databases · no permission missing".
 */
export function probeFacts(result: ProbeResult): string {
  const missing = result.missingPermissions.length;

  return [
    productName(result.version, result.updateLevel),
    result.edition.replace(/ Edition.*$/, ""),
    `${result.databases.toLocaleString("en")} ${result.databases === 1 ? "database" : "databases"}`,
    missing === 0 ? "no permission missing" : `${missing} ${missing === 1 ? "permission" : "permissions"} missing`,
  ].join(" · ");
}

/** Why a probe failed, and what to do about it. */
export interface FailureWords {
  /** The short title, such as "Login failed". */
  title: string;
  /** What to do. */
  fix: string;
}

const failures: Record<ProbeFailure, FailureWords> = {
  "no-agent": {
    title: "No agent to run the test",
    fix: "Choose the agent that reads the server, or add one on Settings → Agents.",
  },
  "agent-offline": {
    title: "The agent is offline",
    fix: "Start the agent's service or container, then test again.",
  },
  unreachable: {
    title: "Server not reachable",
    fix: "Check the host and the port or instance, and that a firewall lets the agent through. A named instance needs UDP 1434 open from the agent.",
  },
  "login-failed": {
    title: "Login failed",
    fix: "Check the login and the password, or run the login script under Permissions first.",
  },
  certificate: {
    title: "Certificate refused",
    fix: "Choose “Trust its certificate” under Encryption, or give the server a certificate the agent trusts.",
  },
  "timed-out": {
    title: "No answer within 2 minutes",
    fix: "The agent did not report back. Check that it is online and has applied its configuration, then test again.",
  },
  other: {
    title: "The test failed",
    fix: "Read the message below, correct the connection, and test again.",
  },
};

/**
 * Why a probe failed, in words, with what to do.
 *
 * @param reason - The reason the server gave.
 * @returns The title and the fix.
 */
export function probeFailureWords(reason: ProbeFailure): FailureWords {
  return failures[reason];
}

/**
 * Elapsed seconds as a clock: `0:07`, `1:42`.
 *
 * @param seconds - The seconds.
 * @returns The clock.
 */
export function elapsedWords(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));

  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}

/**
 * The time SQL Server spends on one run of a read, as precise as it matters: `63 ms`, `1.4 s`, or a dash.
 *
 * @param seconds - The seconds per run, or null before any run.
 * @returns The words.
 */
export function runTimeWords(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }

  return seconds < 1 ? `${Math.round(seconds * 1000)} ms` : `${Number(seconds.toFixed(1))} s`;
}

/**
 * How long the Collector has run: `3 d 14 h`, `5 h 12 min`, `42 min`.
 *
 * @param seconds - The uptime, or null.
 * @returns The words, or a dash.
 */
export function uptimeWords(seconds: number | null): string {
  if (seconds === null) {
    return "—";
  }

  if (seconds < 3600) {
    return `${Math.max(1, Math.round(seconds / 60))} min`;
  }

  if (seconds < 86_400) {
    const minutes = Math.round((seconds % 3600) / 60);

    return minutes > 0 ? `${Math.floor(seconds / 3600)} h ${minutes} min` : `${Math.floor(seconds / 3600)} h`;
  }

  const hours = Math.floor((seconds % 86_400) / 3600);

  return hours > 0 ? `${Math.floor(seconds / 86_400)} d ${hours} h` : `${Math.floor(seconds / 86_400)} d`;
}

/** How the agent's configuration stands, with its tone. */
export interface ConfigWords {
  /** The headline, such as "Valid". */
  title: string;
  /** Its tone. */
  tone: "ok" | "bad" | "wait";
}

const configs: Record<CollectorConfigState, ConfigWords> = {
  applied: { title: "Valid", tone: "ok" },
  applying: { title: "Applying", tone: "wait" },
  failed: { title: "Refused", tone: "bad" },
  unknown: { title: "Not known yet", tone: "wait" },
};

/**
 * The agent's configuration status in words.
 *
 * @param state - The status OpAMP reported.
 * @returns The headline and tone.
 */
export function configWords(state: CollectorConfigState): ConfigWords {
  return configs[state];
}

/**
 * One entry of the monitoring history in a sentence.
 *
 * @param item - The entry.
 * @returns Such as "Rolled back from 0.2.0 to 0.1.0" or "Modules changed".
 */
export function historyWords(item: IntegrationHistoryItem): string {
  const change = item.from && item.to ? ` from ${item.from} to ${item.to}` : item.to ? ` to ${item.to}` : "";

  switch (item.kind) {
    case "created":
      return `Added${item.to ? ` with template ${item.to}` : ""}`;
    case "version":
      return item.from && item.to && compareVersions(item.to, item.from) < 0
        ? `Rolled back${change}`
        : `Template updated${change}`;
    case "upgrade":
      return `Moved at Veyra's upgrade${change}`;
    case "modules":
      return `Modules changed${change}`;
    case "connection":
      return "Connection changed";
    case "probe":
      return `Validated${item.to ? `: ${item.to}` : ""}`;
    default:
      return `${item.kind.charAt(0).toUpperCase()}${item.kind.slice(1)}${change}`;
  }
}

/**
 * Compares two dotted version numbers.
 *
 * @param left - One version, such as `0.2.0`.
 * @param right - The other.
 * @returns Below zero when left is older, above zero when newer, zero when equal.
 */
export function compareVersions(left: string, right: string): number {
  const a = left.split(".").map((part) => Number.parseInt(part, 10) || 0);
  const b = right.split(".").map((part) => Number.parseInt(part, 10) || 0);

  for (let index = 0; index < Math.max(a.length, b.length); index++) {
    const difference = (a[index] ?? 0) - (b[index] ?? 0);

    if (difference !== 0) {
      return difference;
    }
  }

  return 0;
}

/**
 * The package page of an integration.
 *
 * @param id - The server's ID.
 * @returns The address.
 */
export function integrationPath(id: string): string {
  return `/integrations/sqlserver/${encodeURIComponent(id)}`;
}

/** The wizard that adds a SQL Server. */
export const newIntegrationPath = "/integrations/sqlserver/new";

/**
 * The script that sets up the login Veyra signs in with. A SQL login gets its password; a Windows login, over NTLM or
 * the agent's own service account, is created from Windows instead. The grants are the same (docs/integrations).
 *
 * @param authentication - How the agent signs in.
 * @param login - The login, the domain login, or the service account.
 * @param password - The SQL login's password; unused for Windows logins.
 * @returns The T-SQL script.
 */
export function setupScript(authentication: SqlServerAuthentication, login: string, password: string): string {
  const name = login.trim() || "veyra_monitor";
  const script = loginScript(name, password);

  if (authentication === "sql") {
    return script;
  }

  const quoted = `[${name.replaceAll("]", "]]")}]`;

  return script.replace(/^CREATE LOGIN .*$/m, `CREATE LOGIN ${quoted} FROM WINDOWS;`);
}

/**
 * The grants alone, for a login that exists and lacks one: the setup script without the statement that creates the
 * login.
 *
 * @param login - The login.
 * @returns The T-SQL script.
 */
export function grantsScript(login: string): string {
  return loginScript(login, "")
    .split("\n")
    .filter((line) => !line.startsWith("CREATE LOGIN"))
    .join("\n");
}
