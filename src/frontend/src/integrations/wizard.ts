import type {
  Agent,
  IntegrationModule,
  IntegrationModuleInfo,
  ProbeInput,
  SqlServerAuthentication,
  SqlServerEncryption,
  SqlServerInput,
  SqlServerPlatform,
} from "../api/types";
import { newPassword } from "../settings/monitoringFormat";
import { isWindowsAgent, targetWords } from "./integrationWords";

/*
 * The SQL Server wizard's draft and its rules (ADR 0028): what each step needs before the next one, and the requests
 * the draft becomes. Every look's wizard lays it out; the rules live here once, so the steps say the same everywhere.
 */

/** The wizard's steps, in order. */
export type WizardStep = "connection" | "permissions" | "modules" | "validation" | "installation";

/** The steps with their names, as the step strip shows them. */
export const wizardSteps: { id: WizardStep; label: string }[] = [
  { id: "connection", label: "Connection" },
  { id: "permissions", label: "Permissions" },
  { id: "modules", label: "Modules" },
  { id: "validation", label: "Validation" },
  { id: "installation", label: "Installation" },
];

/** A default instance is reached on its port; a named one through the SQL Server Browser. */
export type InstanceKind = "default" | "named";

/** Everything the wizard asks, as typed so far. */
export interface WizardDraft {
  /** Default or named instance. */
  instanceKind: InstanceKind;
  /** The host name or IP address. */
  address: string;
  /** The port as typed, for a default instance. */
  port: string;
  /** The instance's name, for a named instance. */
  instance: string;
  /** The kind of SQL Server. */
  platform: SqlServerPlatform;
  /** The database, for an Azure SQL Database. */
  database: string;
  /** How the agent signs in. */
  authentication: SqlServerAuthentication;
  /** The SQL login, the domain login, or the agent's service account. */
  login: string;
  /** The password; unused for integrated authentication. */
  password: string;
  /** The agent that reads the server. */
  agentId: string;
  /** The environment its telemetry is filed under. */
  environment: string;
  /** How the connection is encrypted. */
  encryption: SqlServerEncryption;
  /** Each module's switch and interval. */
  modules: IntegrationModule[];
  /** Its name in Veyra as typed; empty to take the one made from the host. */
  name: string;
}

/** What the rules need besides the draft. */
export interface WizardContext {
  /** The agents that could read the server. */
  agents: Agent[];
  /** The template's modules. */
  catalog: IntegrationModuleInfo[];
}

/**
 * The modules as a new server takes them: every module on, at its default interval.
 *
 * @param catalog - The template's modules.
 * @returns The modules.
 */
export function defaultModules(catalog: IntegrationModuleInfo[]): IntegrationModule[] {
  return catalog.map((module) => ({ id: module.id, enabled: true, intervalSeconds: module.defaultIntervalSeconds }));
}

/**
 * A fresh draft: a default instance on 1433, SQL authentication with a password made here, the built-in agent or the
 * first one, production, encryption required, and every module at its default.
 *
 * @param agents - The agents.
 * @param catalog - The template's modules.
 * @param random - Makes the password; the browser's generator unless a test passes its own.
 * @returns The draft.
 */
export function newDraft(
  agents: Agent[],
  catalog: IntegrationModuleInfo[],
  random?: Parameters<typeof newPassword>[0],
): WizardDraft {
  const agent = agents.find((one) => one.placement === "BuiltIn") ?? agents[0];

  return {
    instanceKind: "default",
    address: "",
    port: "1433",
    instance: "",
    platform: "SqlServer",
    database: "",
    authentication: "sql",
    login: "veyra_monitor",
    password: newPassword(random),
    agentId: agent?.id ?? "",
    environment: "production",
    encryption: "Required",
    modules: defaultModules(catalog),
    name: "",
  };
}

/**
 * The name a server takes in Veyra unless one is typed: the host's first label, or the whole IP address, with the
 * instance after a dash, in the letters a name may hold.
 *
 * @param address - The host.
 * @param instance - The named instance, or empty.
 * @returns Such as `sql-prod-01` or `sql-prod-01-reports`.
 */
export function suggestedName(address: string, instance: string): string {
  const host = address.trim();
  const base = /^\d+(\.\d+){3}$/.test(host) ? host : (host.split(".")[0] ?? "");
  const full = instance.trim() ? `${base}-${instance.trim().toLowerCase()}` : base;

  return full.replace(/[^A-Za-z0-9._-]/g, "-").slice(0, 63);
}

/**
 * The name the server is saved under: the typed one, or the one made from the host.
 *
 * @param draft - The draft.
 * @returns The name.
 */
export function nameOf(draft: WizardDraft): string {
  return draft.name.trim() || suggestedName(draft.address, draft.instanceKind === "named" ? draft.instance : "");
}

/**
 * Where the agent connects, as the summary says it.
 *
 * @param draft - The draft.
 * @returns Such as `sql-prod-01:1433` or `sql-prod-01\REPORTS`.
 */
export function draftTarget(draft: WizardDraft): string {
  const host = draft.address.trim();

  // A host name is said by its first label, as people name servers; an IP address stays whole.
  return targetWords(
    /^\d+(\.\d+){3}$/.test(host) ? host : (host.split(".")[0] ?? host),
    draft.instanceKind === "named" ? draft.instance.trim() || "…" : null,
    draft.port,
  );
}

/** Whether a port is a whole number from 1 to 65535. */
function validPort(port: string): boolean {
  return /^\d{1,5}$/.test(port.trim()) && Number(port) >= 1 && Number(port) <= 65_535;
}

/**
 * What a step still needs before the wizard moves on, in words the step shows. An empty list lets it go on.
 *
 * @param draft - The draft.
 * @param step - The step.
 * @param context - The agents and the template's modules.
 * @returns The problems, in the order of the step's fields.
 */
export function stepProblems(draft: WizardDraft, step: WizardStep, context: WizardContext): string[] {
  const problems: string[] = [];

  if (step === "connection") {
    const agent = context.agents.find((one) => one.id === draft.agentId);

    if (!draft.address.trim()) {
      problems.push("Enter the server's host.");
    } else if (/[\s\\/]/.test(draft.address.trim())) {
      problems.push("Enter the host alone; a named instance goes in its own field.");
    }

    if (draft.instanceKind === "default" && !validPort(draft.port)) {
      problems.push("The port is a number from 1 to 65535.");
    }

    if (draft.instanceKind === "named" && !/^[A-Za-z_][A-Za-z0-9_$]{0,15}$/.test(draft.instance.trim())) {
      problems.push("Enter the instance's name: up to 16 letters, digits, underscores, or dollar signs.");
    }

    if (draft.platform === "AzureSqlDatabase" && !draft.database.trim()) {
      problems.push("Enter the database an Azure SQL Database is read in.");
    }

    if (!draft.login.trim()) {
      problems.push(draft.authentication === "integrated" ? "Enter the agent's service account." : "Enter the login.");
    } else if (draft.authentication === "ntlm" && !/[\\@]/.test(draft.login)) {
      problems.push("A Windows login names its domain, as CORP\\veyra_monitor.");
    }

    if (draft.authentication !== "integrated" && !draft.password) {
      problems.push("Enter the password.");
    }

    if (!agent) {
      problems.push("Choose the agent that reads the server.");
    } else if (draft.authentication === "integrated" && !isWindowsAgent(agent)) {
      problems.push("Integrated Windows authentication needs an agent on Windows.");
    }

    if (!draft.environment.trim()) {
      problems.push("Enter the environment.");
    }
  }

  if (step === "modules") {
    for (const info of context.catalog) {
      const module = draft.modules.find((one) => one.id === info.id);

      if (info.required && !module?.enabled) {
        problems.push(`${info.name} is always on.`);
      }

      if (module?.enabled && !info.choices.includes(module.intervalSeconds)) {
        problems.push(`Choose one of ${info.name}'s intervals.`);
      }
    }
  }

  if (step === "installation" && !/^[A-Za-z0-9._-]{1,63}$/.test(nameOf(draft))) {
    problems.push("The name holds up to 63 letters, digits, dots, dashes, and underscores.");
  }

  return problems;
}

/**
 * The furthest step the wizard may open: every step before it is complete.
 *
 * @param draft - The draft.
 * @param context - The agents and the template's modules.
 * @returns The index of the first step with a problem, or the last step.
 */
export function reachableStep(draft: WizardDraft, context: WizardContext): number {
  const open = wizardSteps.findIndex((step) => stepProblems(draft, step.id, context).length > 0);

  return open < 0 ? wizardSteps.length - 1 : open;
}

/**
 * The connection fields alone, as a key: a probe answers for the connection it tried, so a change makes its answer
 * stale.
 *
 * @param draft - The draft.
 * @returns The key.
 */
export function connectionKey(draft: WizardDraft): string {
  return JSON.stringify(probeInputOf(draft));
}

/**
 * The probe that tests the draft's connection before saving.
 *
 * @param draft - The draft.
 * @returns The probe's request.
 */
export function probeInputOf(draft: WizardDraft): ProbeInput {
  const named = draft.instanceKind === "named";

  return {
    agentId: draft.agentId || null,
    serverId: null,
    platform: draft.platform,
    address: draft.address.trim(),
    port: named ? null : Number(draft.port),
    instance: named ? draft.instance.trim() : null,
    authentication: draft.authentication,
    login: draft.login.trim(),
    password: draft.authentication === "integrated" ? null : draft.password,
    encryption: draft.encryption,
    database: draft.platform === "AzureSqlDatabase" ? draft.database.trim() : null,
  };
}

/**
 * The server the wizard saves.
 *
 * @param draft - The draft.
 * @returns The request of `POST /api/v1/sql-servers`.
 */
export function serverInputOf(draft: WizardDraft): SqlServerInput {
  const named = draft.instanceKind === "named";

  return {
    name: nameOf(draft),
    address: draft.address.trim(),
    // A named instance is found through the SQL Server Browser; the port stays at the default for the record.
    port: named || !validPort(draft.port) ? 1433 : Number(draft.port),
    environment: draft.environment.trim(),
    agentId: draft.agentId || null,
    login: draft.login.trim(),
    password: draft.authentication === "integrated" ? null : draft.password,
    encryption: draft.encryption,
    platform: draft.platform,
    database: draft.platform === "AzureSqlDatabase" ? draft.database.trim() : null,
    instance: named ? draft.instance.trim() : null,
    authentication: draft.authentication,
    modules: draft.modules,
  };
}

/**
 * Switches a module on or off, or sets its interval. A required module stays on.
 *
 * @param modules - The modules.
 * @param catalog - The template's modules, to know which are required.
 * @param id - The module.
 * @param change - Its new switch or interval.
 * @returns The modules with the change.
 */
export function changeModule(
  modules: IntegrationModule[],
  catalog: IntegrationModuleInfo[],
  id: string,
  change: Partial<Omit<IntegrationModule, "id">>,
): IntegrationModule[] {
  const required = catalog.find((info) => info.id === id)?.required ?? false;

  return modules.map((module) =>
    module.id === id ? { ...module, ...change, enabled: required ? true : (change.enabled ?? module.enabled) } : module,
  );
}
