import { describe, expect, it } from "vitest";

import { builtInAgent } from "../settings/testMonitoring";
import {
  agentWords,
  collectionWords,
  compareVersions,
  configWords,
  elapsedWords,
  grantsScript,
  historyWords,
  integrationPath,
  intervalWords,
  isWindowsAgent,
  modulesWords,
  probeFacts,
  probeFailureWords,
  productName,
  readerWords,
  runTimeWords,
  setupScript,
  targetWords,
  uptimeWords,
  versionWords,
} from "./integrationWords";
import { probeResult, prodIntegration, reportIntegration, windowsAgent } from "./testIntegrations";

describe("the words of the integration screens", () => {
  it("isWindowsAgent_ByOperatingSystem_TellsWindowsFromLinux", () => {
    expect(isWindowsAgent(windowsAgent)).toBe(true);
    expect(isWindowsAgent(builtInAgent)).toBe(false);
    expect(isWindowsAgent({ ...builtInAgent, operatingSystem: null })).toBe(false);
    expect(isWindowsAgent(undefined)).toBe(false);
  });

  it("agentWords_EachAgent_NamesItWithItsSystem", () => {
    expect(agentWords(builtInAgent)).toBe("Built-in agent, beside Veyra");
    expect(agentWords(windowsAgent)).toBe("sql-prod agent (Windows)");
    expect(agentWords({ ...windowsAgent, operatingSystem: "Ubuntu 24.04" })).toBe("sql-prod agent (Linux)");
    expect(agentWords({ ...windowsAgent, operatingSystem: null })).toBe("sql-prod agent");
  });

  it("readerWords_AgentName_ReadsInASentence", () => {
    expect(readerWords("sql-prod agent")).toBe("the sql-prod agent");
    expect(readerWords("Built in")).toBe("the agent Built in");
  });

  it("targetWords_DefaultOrNamedInstance_UsesThePortOrTheInstance", () => {
    expect(targetWords("sql-prod-01", null, 1433)).toBe("sql-prod-01:1433");
    expect(targetWords("sql-prod-01", "REPORTS", 1433)).toBe("sql-prod-01\\REPORTS");
    expect(targetWords(" ", null, "1433")).toBe("the server:1433");
  });

  it("intervalWords_EachSize_UsesTheLargestUnitThatFits", () => {
    expect(intervalWords(10)).toBe("10 s");
    expect(intervalWords(60)).toBe("1 min");
    expect(intervalWords(90)).toBe("1 min 30 s");
    expect(intervalWords(900)).toBe("15 min");
    expect(intervalWords(3600)).toBe("1 h");
    expect(intervalWords(21_600)).toBe("6 h");
    expect(intervalWords(86_400)).toBe("1 d");
  });

  it("collectionWords_SwitchedOnModules_SpanShortestToLongest", () => {
    expect(collectionWords(prodIntegration.modules)).toBe("10 s – 15 min");
    expect(collectionWords([{ id: "core", enabled: true, intervalSeconds: 15 }])).toBe("every 15 s");
    expect(collectionWords([{ id: "core", enabled: false, intervalSeconds: 15 }])).toBe("—");
  });

  it("modulesWords_Modules_CountsTheSwitchedOnOnes", () => {
    expect(modulesWords(prodIntegration.modules)).toBe("6 of 9");
    expect(modulesWords(reportIntegration.modules)).toBe("9 of 9");
  });

  it("versionWords_CurrentOrBehind_SaysWhetherAnUpdateIsThere", () => {
    expect(versionWords(prodIntegration)).toEqual({ text: "0.2.0 · current", behind: false });
    expect(versionWords(reportIntegration)).toEqual({ text: "0.1.0 · 0.2.0 available", behind: true });
  });

  it("productName_KnownMajorVersion_NamesTheYearAndUpdate", () => {
    expect(productName("16.0.4135.4", "CU14")).toBe("SQL Server 2022 CU14 (16.0.4135.4)");
    expect(productName("17.0.1000.7", null)).toBe("SQL Server 2025 (17.0.1000.7)");
    expect(productName("99.0.1", null)).toBe("SQL Server (99.0.1)");
  });

  it("probeFacts_Success_SaysProductEditionDatabasesAndPermissions", () => {
    const result = probeResult;

    expect(probeFacts(result)).toBe(
      "SQL Server 2022 CU14 (16.0.4135.4) · Enterprise · 96 databases · no permission missing",
    );
    expect(probeFacts({ ...result, databases: 1, missingPermissions: ["VIEW SERVER STATE"] })).toBe(
      "SQL Server 2022 CU14 (16.0.4135.4) · Enterprise · 1 database · 1 permission missing",
    );
  });

  it("probeFailureWords_EachReason_HasATitleAndAFix", () => {
    for (const reason of [
      "no-agent",
      "agent-offline",
      "unreachable",
      "login-failed",
      "certificate",
      "timed-out",
      "other",
    ] as const) {
      const words = probeFailureWords(reason);

      expect(words.title.length, reason).toBeGreaterThan(0);
      expect(words.fix.length, reason).toBeGreaterThan(0);
    }

    expect(probeFailureWords("unreachable").fix).toContain("UDP 1434");
    expect(probeFailureWords("login-failed").title).toBe("Login failed");
  });

  it("elapsedWords_Seconds_ReadsAsAClock", () => {
    expect(elapsedWords(0)).toBe("0:00");
    expect(elapsedWords(7.8)).toBe("0:07");
    expect(elapsedWords(102)).toBe("1:42");
  });

  it("runTimeWords_SecondsPerRun_InMillisecondsBelowASecond", () => {
    expect(runTimeWords(0.063)).toBe("63 ms");
    expect(runTimeWords(1.42)).toBe("1.4 s");
    expect(runTimeWords(null)).toBe("—");
  });

  it("uptimeWords_Seconds_InDaysHoursOrMinutes", () => {
    expect(uptimeWords(3 * 86_400 + 14 * 3600)).toBe("3 d 14 h");
    expect(uptimeWords(5 * 3600 + 12 * 60)).toBe("5 h 12 min");
    expect(uptimeWords(42 * 60)).toBe("42 min");
    expect(uptimeWords(null)).toBe("—");
  });

  it("configWords_EachState_HasItsHeadlineAndTone", () => {
    expect(configWords("applied")).toEqual({ title: "Valid", tone: "ok" });
    expect(configWords("failed").tone).toBe("bad");
    expect(configWords("applying").tone).toBe("wait");
  });

  it("historyWords_VersionChanges_SayUpdateOrRollBack", () => {
    const at = "2026-11-25T22:14:00Z";

    expect(historyWords({ at, by: null, kind: "version", from: "0.1.0", to: "0.2.0" })).toBe(
      "Template updated from 0.1.0 to 0.2.0",
    );
    expect(historyWords({ at, by: null, kind: "version", from: "0.2.0", to: "0.1.0" })).toBe(
      "Rolled back from 0.2.0 to 0.1.0",
    );
    expect(historyWords({ at, by: null, kind: "created", from: null, to: "0.1.0" })).toBe("Added with template 0.1.0");
    expect(historyWords({ at, by: null, kind: "renamed", from: "a", to: "b" })).toBe("Renamed from a to b");
  });

  it("compareVersions_DottedNumbers_ComparesEachPart", () => {
    expect(compareVersions("0.10.0", "0.9.0")).toBeGreaterThan(0);
    expect(compareVersions("0.1.0", "0.2.0")).toBeLessThan(0);
    expect(compareVersions("1.0", "1.0.0")).toBe(0);
  });

  it("integrationPath_Id_IsThePackagePage", () => {
    expect(integrationPath("s1")).toBe("/integrations/sqlserver/s1");
  });

  it("setupScript_WindowsLogin_CreatesItFromWindowsWithTheSameGrants", () => {
    const sql = setupScript("sql", "veyra_monitor", "Secret1");
    const ntlm = setupScript("ntlm", "CORP\\veyra_monitor", "ignored");

    expect(sql).toContain("CREATE LOGIN [veyra_monitor] WITH PASSWORD = N'Secret1';");
    expect(ntlm).toContain("CREATE LOGIN [CORP\\veyra_monitor] FROM WINDOWS;");
    expect(ntlm).not.toContain("PASSWORD");
    expect(ntlm).toContain("GRANT VIEW SERVER STATE TO [CORP\\veyra_monitor];");
  });

  it("grantsScript_Login_LeavesOutCreatingIt", () => {
    const script = grantsScript("veyra_monitor");

    expect(script).not.toContain("CREATE LOGIN");
    expect(script).toContain("GRANT VIEW SERVER STATE TO [veyra_monitor];");
  });
});
