import type { Agent, AgentState, SqlServer, SqlServerEncryption, SqlServerState } from "../api/types";

/** The colour a state is shown in: fine, needs someone, or not yet known. */
export type Tone = "ok" | "bad" | "wait";

/** A state as the page says it. */
export interface StateWords {
  /** The short label on the state's chip. */
  label: string;
  /** Its colour. */
  tone: Tone;
}

/** One line of the check that follows saving a server. */
export interface CheckLine {
  /** What is checked, such as "Reached sql-prod-01:1433". */
  text: string;
  /** Whether it worked: true, false, or null while unknown. */
  passed: boolean | null;
  /** A note after it, such as what to do about a failure. */
  note?: string;
}

const serverWords: Record<SqlServerState, StateWords> = {
  AgentNotConnected: { label: "Agent not connected", tone: "wait" },
  AgentOffline: { label: "Agent offline", tone: "bad" },
  ConfigurationFailed: { label: "Agent refused its settings", tone: "bad" },
  LoginFailed: { label: "Login failed", tone: "bad" },
  Unreachable: { label: "Not reachable", tone: "bad" },
  CertificateRejected: { label: "Certificate refused", tone: "bad" },
  GrantMissing: { label: "A grant is missing", tone: "bad" },
  Failing: { label: "Reading fails", tone: "bad" },
  Receiving: { label: "Receiving", tone: "ok" },
  WaitingForData: { label: "Waiting for first data", tone: "wait" },
};

const agentWords: Record<AgentState, StateWords> = {
  NotConnected: { label: "Not connected yet", tone: "wait" },
  Offline: { label: "Offline", tone: "bad" },
  ConfigurationFailed: { label: "Refused its settings", tone: "bad" },
  Unhealthy: { label: "Unhealthy", tone: "bad" },
  Online: { label: "Online", tone: "ok" },
};

/** The names of the three ways to encrypt, as the page offers them. */
export const encryptionLabels: Record<SqlServerEncryption, string> = {
  Required: "Required",
  TrustServerCertificate: "Trust its certificate",
  Off: "Off",
};

/**
 * A server's state in words.
 *
 * @param state - The state.
 * @returns Its label and colour.
 */
export function serverStateWords(state: SqlServerState): StateWords {
  return serverWords[state];
}

/**
 * An agent's state in words.
 *
 * @param state - The state.
 * @returns Its label and colour.
 */
export function agentStateWords(state: AgentState): StateWords {
  return agentWords[state];
}

/**
 * The words for the agent that reads a server, as a sentence names it.
 *
 * @param server - The server.
 * @param agents - Every agent, to tell the built-in one.
 * @returns "the built-in agent", or "the agent" and its name.
 */
export function readerOf(server: SqlServer, agents: Agent[]): string {
  const agent = agents.find((one) => one.id === server.agentId);

  return agent?.placement === "BuiltIn" ? "the built-in agent" : `the agent ${server.agentName}`;
}

/** The words with a capital first letter, to start a sentence. */
function capital(words: string): string {
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * How long ago something happened, in a few words.
 *
 * @param iso - When, in ISO 8601; null for never.
 * @param now - The current time.
 * @returns Such as "8 s ago", "3 min ago", or "2 h ago"; "never" for null.
 */
export function ago(iso: string | null, now: Date): string {
  if (!iso) {
    return "never";
  }

  const seconds = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 1000));

  if (seconds < 60) {
    return `${seconds} s ago`;
  }

  if (seconds < 3600) {
    return `${Math.floor(seconds / 60)} min ago`;
  }

  if (seconds < 86_400) {
    return `${Math.floor(seconds / 3600)} h ago`;
  }

  return `${Math.floor(seconds / 86_400)} d ago`;
}

/**
 * What a server's state means and what to do about it, in a sentence or two.
 *
 * @param server - The server with its state.
 * @param now - The current time.
 * @param reader - The words for the agent that reads it, from {@link readerOf}.
 * @returns The sentence.
 */
export function serverExplanation(server: SqlServer, now: Date, reader = `the agent ${server.agentName}`): string {
  switch (server.state) {
    case "AgentNotConnected":
      return `${capital(reader)} has not connected yet. In Veyra's Docker Compose deployment it starts beside Veyra by itself.`;
    case "AgentOffline":
      return `${capital(reader)} last reported ${ago(server.since, now)}. Check that its container or service runs.`;
    case "ConfigurationFailed":
      return `The agent refused the settings Veyra sent and keeps its last ones: ${server.detail ?? "no reason given"}.`;
    case "LoginFailed":
      return `SQL Server refused the login ${server.login}: a wrong password, or the login was never created. Enter the password again, or run the login script again.`;
    case "Unreachable":
      return `The agent cannot reach ${server.address}:${server.port}. Check the address and the port, and that a firewall lets the agent through.`;
    case "CertificateRejected":
      return "The agent does not trust the server's certificate. Choose “Trust its certificate”, or give the server a certificate the agent trusts.";
    case "GrantMissing":
      return "The login works, but a grant is missing, so some figures are missing. Run the login script again.";
    case "Failing":
      return `Reading fails: ${server.detail ?? "no reason given"}.`;
    case "Receiving":
      return `Last figures ${ago(server.since, now)}.`;
    case "WaitingForData":
      return `Saved ${ago(server.changedAt, now)}. The agent picks it up within half a minute; the first figures follow.`;
  }
}

/**
 * The check after saving a server, line by line, as far as its state tells.
 *
 * @param server - The server with its state.
 * @param reader - The words for the agent that reads it, from {@link readerOf}.
 * @returns The lines, in the order a connection gets that far.
 */
export function checkLines(server: SqlServer, reader = `the agent ${server.agentName}`): CheckLine[] {
  const agentFails = ["AgentNotConnected", "AgentOffline", "ConfigurationFailed"].includes(server.state);
  const steps: SqlServerState[] = ["Unreachable", "CertificateRejected", "LoginFailed", "GrantMissing"];
  const failedAt = server.state === "Failing" ? steps.length - 1 : steps.indexOf(server.state);
  const known = server.state === "Receiving" || failedAt >= 0;

  const line = (index: number, text: string, note?: string): CheckLine => ({
    text,
    passed: !known ? null : failedAt < 0 || index < failedAt ? true : index === failedAt ? false : null,
    note: known && index === failedAt ? note : undefined,
  });

  return [
    {
      text: `${capital(reader)} has the settings`,
      passed: agentFails ? false : server.state === "WaitingForData" ? null : true,
      note: agentFails ? serverStateWords(server.state).label : undefined,
    },
    line(0, `Reached ${server.address}:${server.port}`, "check the address, the port, and the firewall"),
    line(
      1,
      server.encryption === "Off" ? "Connected without encryption" : "Encrypted connection",
      "the certificate is not trusted",
    ),
    line(2, `Logged in as ${server.login}`, "wrong password, or the login does not exist"),
    line(
      3,
      "Read every figure",
      server.state === "Failing" ? (server.detail ?? undefined) : "run the login script again: a grant is missing",
    ),
  ];
}

/**
 * The script that creates the login with exactly the grants Veyra needs (docs/integrations/sqlserver.md). Only an
 * administrator of the server can run it, so the page shows it ready to paste into SQL Server Management Studio.
 *
 * @param login - The login's name.
 * @param password - Its password.
 * @returns The T-SQL script.
 */
export function loginScript(login: string, password: string): string {
  const name = `[${login.replaceAll("]", "]]")}]`;
  const secret = password.replaceAll("'", "''");

  return [
    "USE master;",
    `CREATE LOGIN ${name} WITH PASSWORD = N'${secret}';`,
    `GRANT VIEW SERVER STATE TO ${name};`,
    `GRANT VIEW ANY DEFINITION TO ${name};`,
    // Query Store, file space, and index names are read in each database (ADR 0020).
    `GRANT CONNECT ANY DATABASE TO ${name};`,
    "GO",
    "",
    "USE msdb;",
    `CREATE USER ${name} FOR LOGIN ${name};`,
    `GRANT SELECT ON dbo.backupset TO ${name};`,
    `GRANT SELECT ON dbo.sysjobs TO ${name};`,
    `GRANT SELECT ON dbo.sysjobhistory TO ${name};`,
    `GRANT EXECUTE ON dbo.agent_datetime TO ${name};`,
    `GRANT SELECT ON dbo.backupmediafamily TO ${name};`,
    `GRANT SELECT ON dbo.sysjobsteps TO ${name};`,
    `GRANT SELECT ON dbo.sysjobactivity TO ${name};`,
    `GRANT SELECT ON dbo.sysjobschedules TO ${name};`,
    `GRANT SELECT ON dbo.sysschedules TO ${name};`,
    `GRANT SELECT ON dbo.suspect_pages TO ${name};`,
    `GRANT SELECT ON dbo.log_shipping_monitor_primary TO ${name};`,
    `GRANT SELECT ON dbo.log_shipping_monitor_secondary TO ${name};`,
    "GO",
  ].join("\n");
}

/**
 * The one line that gives an existing login a new password.
 *
 * @param login - The login's name.
 * @param password - Its new password.
 * @returns The T-SQL statement.
 */
export function passwordScript(login: string, password: string): string {
  return `ALTER LOGIN [${login.replaceAll("]", "]]")}] WITH PASSWORD = N'${password.replaceAll("'", "''")}';`;
}

// No letters that are easy to mix up when read aloud, and nothing SQL or a URL treats specially.
const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

/**
 * A strong password for the login, made with the browser's cryptographic generator. It has capitals, small letters,
 * and digits, so it meets SQL Server's password policy.
 *
 * @param random - Fills an array with random bytes; the browser's generator unless a test passes its own.
 * @returns 24 characters.
 */
export function newPassword(
  random: (bytes: Uint8Array<ArrayBuffer>) => Uint8Array<ArrayBuffer> = (bytes) => crypto.getRandomValues(bytes),
): string {
  // Bytes at or above the largest multiple of the alphabet's length are dropped, so every character is equally likely.
  const limit = 256 - (256 % alphabet.length);

  for (;;) {
    let password = "";

    while (password.length < 24) {
      for (const byte of random(new Uint8Array(32))) {
        if (byte < limit && password.length < 24) {
          password += alphabet[byte % alphabet.length];
        }
      }
    }

    if (/[A-Z]/.test(password) && /[a-z]/.test(password) && /[0-9]/.test(password)) {
      return password;
    }
  }
}

/** The systems Veyra's installers put an agent on. */
export type InstallPlatform = "Windows" | "Linux";

/**
 * The one line that installs an agent in another network, to run on a machine there: in PowerShell as an
 * administrator on Windows, as root on Linux.
 *
 * @param platform - The machine's system.
 * @param origin - Veyra's address as that machine reaches it, such as `https://veyra.example.com`.
 * @param code - The agent's one-time code.
 * @returns The command.
 */
export function installCommand(platform: InstallPlatform, origin: string, code: string): string {
  return platform === "Windows"
    ? `irm ${origin}/v1/agents/install.ps1 | iex; Install-VeyraAgent -Code ${code}`
    : `curl -fsSL ${origin}/v1/agents/install.sh | sudo sh -s -- --code ${code}`;
}

/**
 * The one line that removes an agent from its machine again.
 *
 * @param platform - The machine's system.
 * @param origin - Veyra's address as that machine reaches it.
 * @returns The command.
 */
export function uninstallCommand(platform: InstallPlatform, origin: string): string {
  return platform === "Windows"
    ? `irm ${origin}/v1/agents/install.ps1 | iex; Uninstall-VeyraAgent`
    : `curl -fsSL ${origin}/v1/agents/install.sh | sudo sh -s -- --uninstall`;
}
