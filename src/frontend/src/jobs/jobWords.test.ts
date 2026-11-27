import { describe, expect, it } from "vitest";

import {
  blockPath,
  blockWords,
  durationWords,
  jobPath,
  logsPath,
  recordsWords,
  runLogsPath,
  sourceWords,
  statusTone,
  statusWords,
  successTone,
  successWords,
} from "./jobWords";
import { customerSync, failedSyncRun, nightlyBackup, orderExport, orderExportDetails } from "./testJobs";

describe("the words of the job screens", () => {
  it("statusWords_EachStatus_HasItsWordAndTone", () => {
    expect(statusWords("succeeded")).toBe("Succeeded");
    expect(statusWords("failed")).toBe("Failed");
    expect(statusWords("running")).toBe("Running");
    expect(statusWords("unknown")).toBe("Unknown");
    expect(statusTone("succeeded")).toBe("ok");
    expect(statusTone("failed")).toBe("bad");
    expect(statusTone("running")).toBe("info");
    expect(statusTone("unknown")).toBe("off");
  });

  it("sourceWords_EachSource_IsNamed", () => {
    expect(sourceWords("trace")).toBe("Traces");
    expect(sourceWords("sqlagent")).toBe("SQL Agent");
  });

  it("successWords_NearlyAll_NeverRoundsUpTo100", () => {
    expect(successWords(0.99996)).toBe("99.9%");
    expect(successWords(93 / 96)).toBe("96.8%");
    expect(successWords(1)).toBe("100%");
    expect(successWords(0.5)).toBe("50%");
    expect(successWords(null)).toBe("—");
  });

  it("successTone_Thresholds_RedBelow90AmberBelow99", () => {
    expect(successTone(0.89)).toBe("bad");
    expect(successTone(0.95)).toBe("warn");
    expect(successTone(0.99)).toBe("ok");
    expect(successTone(null)).toBeNull();
  });

  it("durationWords_EachSize_IsAsPreciseAsItMatters", () => {
    expect(durationWords(0.24)).toBe("240 ms");
    expect(durationWords(4.24)).toBe("4.2 s");
    expect(durationWords(38.4)).toBe("38 s");
    expect(durationWords(74.2)).toBe("1 min 14 s");
    expect(durationWords(2400)).toBe("40 min");
    expect(durationWords(4320)).toBe("1 h 12 min");
    expect(durationWords(7200)).toBe("2 h");
    expect(durationWords(null)).toBe("—");
  });

  it("recordsWords_WithAndWithoutTotal_SaysWhatIsKnown", () => {
    expect(recordsWords(31_200, 50_000)).toBe("31,200 of 50,000");
    expect(recordsWords(31_200, null)).toBe("31,200");
    expect(recordsWords(null, 50_000)).toBe("— of 50,000");
    expect(recordsWords(null, null)).toBe("—");
  });
});

describe("the links of the job screens", () => {
  it("blockPath_Service_OpensTheServiceScreen", () => {
    expect(blockPath(customerSync.block)).toBe("/metrics/services/CustomerSync");
    expect(blockWords(customerSync.block)).toBe("CustomerSync");
  });

  it("blockPath_DatabaseServer_OpensItsJobsTab", () => {
    expect(blockPath(nightlyBackup.block)).toBe("/metrics/databases/microsoft.sql_server/sql-prod-01?tab=jobs");
    expect(blockWords(nightlyBackup.block)).toBe("sql-prod-01");
  });

  it("jobPath_Id_IsEncoded", () => {
    expect(jobPath("job-customersync")).toBe("/jobs/job-customersync");
    expect(jobPath("a/b")).toBe("/jobs/a%2Fb");
  });

  it("runLogsPath_FinishedRun_SpansTheRunWithAMinuteEachSide", () => {
    const path = runLogsPath(customerSync, failedSyncRun);
    const params = new URLSearchParams(path?.split("?")[1]);

    expect(path?.startsWith("/logs?")).toBe(true);
    expect(params.get("q")).toBe("service:CustomerSync");
    expect(params.get("from")).toBe("2026-11-26T14:44:00.000Z");
    expect(params.get("to")).toBe("2026-11-26T14:47:14.200Z");
  });

  it("runLogsPath_RunningRun_EndsAMinuteAfterNow", () => {
    const run = orderExportDetails.runs[0];
    const now = new Date("2026-11-26T15:00:00.000Z");
    const params = new URLSearchParams(run ? runLogsPath(orderExport, run, now)?.split("?")[1] : "");

    expect(params.get("q")).toBe("service:orders-api");
    expect(params.get("to")).toBe("2026-11-26T15:01:00.000Z");
  });

  it("logsPath_SqlAgentJob_HasNoLogs", () => {
    expect(logsPath(nightlyBackup, "2026-11-26T00:00:00.000Z", "2026-11-26T01:00:00.000Z")).toBeNull();
  });

  it("logsPath_NameWithSpaces_IsQuoted", () => {
    const job = { ...customerSync, block: { ...customerSync.block, name: "Customer Sync" } };
    const path = logsPath(job, "2026-11-26T00:00:00.000Z", "2026-11-26T01:00:00.000Z");

    expect(new URLSearchParams(path?.split("?")[1]).get("q")).toBe('service:"Customer Sync"');
  });
});
