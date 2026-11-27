import { describe, expect, it } from "vitest";

import { builtInAgent } from "../settings/testMonitoring";
import { moduleCatalog, windowsAgent, wizardAgents } from "./testIntegrations";
import {
  changeModule,
  connectionKey,
  draftTarget,
  nameOf,
  newDraft,
  probeInputOf,
  reachableStep,
  serverInputOf,
  stepProblems,
  suggestedName,
  type WizardContext,
  type WizardDraft,
} from "./wizard";

const context: WizardContext = { agents: wizardAgents, catalog: moduleCatalog };

/** Bytes that always pick the same characters, so the password is known. */
const fixed = (bytes: Uint8Array<ArrayBuffer>) => bytes.map((_, index) => index * 7);

/** A draft that passes every step, for sql-prod-01. */
function filled(patch: Partial<WizardDraft> = {}): WizardDraft {
  return { ...newDraft(wizardAgents, moduleCatalog, fixed), address: "sql-prod-01.corp.local", ...patch };
}

describe("the SQL Server wizard", () => {
  it("newDraft_Defaults_BuiltInAgentSqlLoginAndEveryModuleAtItsDefault", () => {
    const draft = newDraft(wizardAgents, moduleCatalog, fixed);

    expect(draft).toMatchObject({
      instanceKind: "default",
      port: "1433",
      authentication: "sql",
      login: "veyra_monitor",
      agentId: builtInAgent.id,
      environment: "production",
      encryption: "Required",
    });
    expect(draft.password).toMatch(/^[A-Za-z0-9]{24}$/);
    expect(draft.modules).toHaveLength(9);
    expect(draft.modules.every((module) => module.enabled)).toBe(true);
    expect(draft.modules.find((module) => module.id === "queries")?.intervalSeconds).toBe(900);
  });

  it("stepProblems_EmptyHost_AsksForIt", () => {
    expect(stepProblems(newDraft(wizardAgents, moduleCatalog), "connection", context)).toEqual([
      "Enter the server's host.",
    ]);
  });

  it("stepProblems_FilledConnection_HasNone", () => {
    expect(stepProblems(filled(), "connection", context)).toEqual([]);
  });

  it("stepProblems_BadPortOrInstance_SaysWhichIsWrong", () => {
    expect(stepProblems(filled({ port: "70000" }), "connection", context)).toEqual([
      "The port is a number from 1 to 65535.",
    ]);
    expect(stepProblems(filled({ instanceKind: "named", port: "x", instance: "" }), "connection", context)).toEqual([
      "Enter the instance's name: up to 16 letters, digits, underscores, or dollar signs.",
    ]);
    expect(stepProblems(filled({ instanceKind: "named", instance: "REPORTS" }), "connection", context)).toEqual([]);
    expect(stepProblems(filled({ address: "sql-prod-01\\REPORTS" }), "connection", context)).toEqual([
      "Enter the host alone; a named instance goes in its own field.",
    ]);
  });

  it("stepProblems_MissingLoginOrPassword_AsksForThem", () => {
    expect(stepProblems(filled({ login: " ", password: "" }), "connection", context)).toEqual([
      "Enter the login.",
      "Enter the password.",
    ]);
  });

  it("stepProblems_NtlmWithoutDomain_AsksForTheDomain", () => {
    expect(stepProblems(filled({ authentication: "ntlm", login: "veyra_monitor" }), "connection", context)).toEqual([
      "A Windows login names its domain, as CORP\\veyra_monitor.",
    ]);
    expect(
      stepProblems(filled({ authentication: "ntlm", login: "CORP\\veyra_monitor" }), "connection", context),
    ).toEqual([]);
  });

  it("stepProblems_IntegratedOnLinuxAgent_RefusesIt", () => {
    const integrated = { authentication: "integrated" as const, login: "CORP\\svc-veyra", password: "" };

    expect(stepProblems(filled({ ...integrated, agentId: builtInAgent.id }), "connection", context)).toEqual([
      "Integrated Windows authentication needs an agent on Windows.",
    ]);
    expect(stepProblems(filled({ ...integrated, agentId: windowsAgent.id }), "connection", context)).toEqual([]);
  });

  it("stepProblems_NoAgentOrEnvironment_AsksForThem", () => {
    expect(stepProblems(filled({ agentId: "", environment: "" }), "connection", context)).toEqual([
      "Choose the agent that reads the server.",
      "Enter the environment.",
    ]);
  });

  it("stepProblems_AzureSqlDatabase_NeedsTheDatabase", () => {
    expect(stepProblems(filled({ platform: "AzureSqlDatabase" }), "connection", context)).toEqual([
      "Enter the database an Azure SQL Database is read in.",
    ]);
  });

  it("stepProblems_CoreOffOrIntervalNotOffered_RefusesTheModules", () => {
    const draft = filled();
    const off = { ...draft, modules: draft.modules.map((m) => (m.id === "core" ? { ...m, enabled: false } : m)) };
    const odd = { ...draft, modules: draft.modules.map((m) => (m.id === "jobs" ? { ...m, intervalSeconds: 7 } : m)) };

    expect(stepProblems(draft, "modules", context)).toEqual([]);
    expect(stepProblems(off, "modules", context)).toEqual(["Core is always on."]);
    expect(stepProblems(odd, "modules", context)).toEqual(["Choose one of Jobs's intervals."]);
  });

  it("stepProblems_BadName_RefusesTheInstallation", () => {
    expect(stepProblems(filled(), "installation", context)).toEqual([]);
    expect(stepProblems(filled({ name: "sql prod" }), "installation", context)).toEqual([
      "The name holds up to 63 letters, digits, dots, dashes, and underscores.",
    ]);
  });

  it("reachableStep_FirstIncompleteStep_IsTheFurthest", () => {
    expect(reachableStep(newDraft(wizardAgents, moduleCatalog), context)).toBe(0);
    expect(reachableStep(filled(), context)).toBe(4);
  });

  it("suggestedName_HostAndInstance_MakeAName", () => {
    expect(suggestedName("sql-prod-01.corp.local", "")).toBe("sql-prod-01");
    expect(suggestedName("10.20.0.14", "")).toBe("10.20.0.14");
    expect(suggestedName("sql-prod-01.corp.local", "REPORTS")).toBe("sql-prod-01-reports");
    expect(nameOf(filled({ name: " sql-main " }))).toBe("sql-main");
    expect(nameOf(filled())).toBe("sql-prod-01");
  });

  it("draftTarget_Draft_NamesTheHostByItsFirstLabel", () => {
    expect(draftTarget(filled())).toBe("sql-prod-01:1433");
    expect(draftTarget(filled({ instanceKind: "named", instance: "REPORTS" }))).toBe("sql-prod-01\\REPORTS");
    expect(draftTarget(filled({ address: "10.20.0.14" }))).toBe("10.20.0.14:1433");
  });

  it("serverInputOf_NamedInstanceWithNtlm_SendsInstanceAuthenticationAndModules", () => {
    const draft = filled({
      instanceKind: "named",
      instance: "REPORTS",
      authentication: "ntlm",
      login: "CORP\\veyra_monitor",
    });
    const input = serverInputOf(draft);

    expect(input).toMatchObject({
      name: "sql-prod-01-reports",
      address: "sql-prod-01.corp.local",
      port: 1433,
      instance: "REPORTS",
      authentication: "ntlm",
      login: "CORP\\veyra_monitor",
      password: draft.password,
      platform: "SqlServer",
      database: null,
    });
    expect(input.modules).toEqual(draft.modules);
  });

  it("serverInputOf_Integrated_SendsNoPassword", () => {
    const input = serverInputOf(
      filled({ authentication: "integrated", agentId: windowsAgent.id, login: "CORP\\svc-veyra" }),
    );

    expect(input.password).toBeNull();
    expect(input.agentId).toBe(windowsAgent.id);
  });

  it("probeInputOf_DefaultInstance_SendsThePortAndNoServer", () => {
    expect(probeInputOf(filled({ port: "14330" }))).toMatchObject({
      serverId: null,
      agentId: builtInAgent.id,
      address: "sql-prod-01.corp.local",
      port: 14_330,
      instance: null,
      authentication: "sql",
    });
  });

  it("connectionKey_ModulesOrName_DoNotMakeTheTestStale", () => {
    const draft = filled();

    expect(connectionKey({ ...draft, name: "other", modules: [] })).toBe(connectionKey(draft));
    expect(connectionKey({ ...draft, port: "1434" })).not.toBe(connectionKey(draft));
  });

  it("changeModule_RequiredModule_StaysOn", () => {
    const draft = filled();
    const core = changeModule(draft.modules, moduleCatalog, "core", { enabled: false });
    const events = changeModule(draft.modules, moduleCatalog, "events", { enabled: false });
    const faster = changeModule(draft.modules, moduleCatalog, "core", { intervalSeconds: 10 });

    expect(core.find((m) => m.id === "core")?.enabled).toBe(true);
    expect(events.find((m) => m.id === "events")?.enabled).toBe(false);
    expect(faster.find((m) => m.id === "core")).toEqual({ id: "core", enabled: true, intervalSeconds: 10 });
  });
});
