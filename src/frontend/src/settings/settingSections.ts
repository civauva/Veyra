import type { Agent, Setting, SettingSection, SqlServer } from "../api/types";
import { serverStateWords } from "./monitoringFormat";
import { describeValue } from "./settingsFormat";

/**
 * Where the screen is: one of the sections; Databases or Agents, which manage what Veyra watches (ADR 0019); or
 * System, which shows what is set where Veyra is installed.
 */
export type SectionId = SettingSection | "Databases" | "Agents" | "System";

/** The sections that hold no settings but pages of their own, shown in either view. */
export const pageSections: SectionId[] = ["Databases", "Agents"];

/** What the Database monitoring and Agents sections summarise. */
export interface Watched {
  /** The SQL Servers agents read. */
  servers: SqlServer[];
  /** The agents. */
  agents: Agent[];
}

/** A section's name and the sentence that says what it holds. */
export interface SectionInfo {
  /** The section. */
  id: SectionId;
  /** Its title. */
  title: string;
  /** What it holds. */
  intro: string;
}

/** The first section, shown when the address names none. */
const data: SectionInfo = {
  id: "Data",
  title: "Data and retention",
  intro: "How long each kind of data is kept, and how much room it may take. Old days are dropped whole, every hour.",
};

/** The sections in screen order, with their titles and a sentence that says what they hold. */
export const sections: SectionInfo[] = [
  data,
  {
    id: "Rules",
    title: "Alert rules",
    intro: "The built-in rules behind every alert and the home's weather. A rule switched off opens no new alerts.",
  },
  {
    id: "Databases",
    title: "Database monitoring",
    intro:
      "The database servers Veyra watches. Add one here: Veyra writes its login script and sends the settings to the agent that reads it. Nothing else needs editing.",
  },
  {
    id: "Agents",
    title: "Agents",
    intro:
      "The agents that read your databases for Veyra: the official OpenTelemetry Collector, set up and configured by Veyra.",
  },
  {
    id: "Email",
    title: "Notifications and email",
    intro: "The email server every email channel sends through, and how queued messages are sent.",
  },
  {
    id: "Security",
    title: "Sign-in and security",
    intro: "Passwords, lockout, how long sessions last, and how long sign-in history is kept.",
  },
  {
    id: "Ingestion",
    title: "Ingestion",
    intro: "How much telemetry may wait in memory to be written before senders are told to retry.",
  },
  {
    id: "Advanced",
    title: "Advanced",
    intro: "Settings most installations never change.",
  },
  {
    id: "System",
    title: "System",
    intro: "Set where Veyra is installed, before the database can be read. Change these in deploy/.env and restart.",
  },
];

/**
 * The title and intro of a section.
 *
 * @param id - The section.
 * @returns Its information; the first section's for an unknown one.
 */
export function sectionInfo(id: SectionId): SectionInfo {
  return sections.find((section) => section.id === id) ?? data;
}

/**
 * Reads the section from the address, falling back to the first.
 *
 * @param value - The `section` query parameter.
 * @returns A known section.
 */
export function sectionFrom(value: string | null): SectionId {
  return sections.find((section) => section.id === value)?.id ?? "Data";
}

/**
 * A short line under a section's name that says what it is set to now, so the list reads as a summary.
 *
 * @param id - The section.
 * @param settings - Every setting, with the values in effect.
 * @param watched - The servers and agents, once loaded.
 * @returns The line.
 */
export function sectionSummary(id: SectionId, settings: Setting[], watched: Watched | null = null): string {
  const value = (key: string) => settings.find((setting) => setting.key === key);
  const shown = (key: string) => {
    const setting = value(key);

    return setting ? describeValue(setting, setting.value) : "—";
  };

  switch (id) {
    case "Data": {
      const days = (key: string) => value(key)?.value ?? "—";

      return `Logs ${days("Veyra:Storage:LogRetentionDays")} · metrics ${days("Veyra:Storage:MetricRetentionDays")} · traces ${days("Veyra:Storage:TraceRetentionDays")} days`;
    }
    case "Rules": {
      const on = settings.filter((setting) => setting.section === "Rules" && setting.label === "On");

      return `${on.filter((setting) => setting.value === "true").length} of ${on.length} rules on`;
    }
    case "Databases": {
      if (!watched) {
        return "SQL Server";
      }

      const count = watched.servers.length;
      const look = watched.servers.filter((server) => serverStateWords(server.state).tone === "bad").length;
      const servers = count === 1 ? "1 SQL Server" : `${count} SQL Servers`;

      return count === 0 ? "No server yet" : look > 0 ? `${servers} · ${look} needs a look` : servers;
    }
    case "Agents": {
      if (!watched) {
        return "Built-in agent";
      }

      const online = watched.agents.filter((agent) => agent.state === "Online").length;

      return watched.agents.length === 0 ? "No agent yet" : `${online} of ${watched.agents.length} online`;
    }
    case "Email":
      return value("Veyra:Alerting:Notifications:Smtp:Host")?.value || "No email server";
    case "Security":
      return `${shown("Veyra:Access:SessionIdleTimeout")} sessions · lockout after ${value("Veyra:Access:LockoutThreshold")?.value ?? "—"}`;
    case "Ingestion":
      return "Queues for logs, metrics, and spans";
    case "Advanced":
      return "Rule checks, paging, caches";
    default:
      return "Database, addresses, keys";
  }
}
