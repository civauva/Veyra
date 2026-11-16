import { describe, expect, it } from "vitest";

import {
  alertTitle,
  databasePath,
  formatDays,
  formatDiskShare,
  formatMeasure,
  formatThreshold,
  ruleName,
  ruleSentence,
  ruleSummary,
  serverName,
  subjectLabel,
} from "./ruleWords";

const service = { kind: "service" as const, name: "checkout-web", part: null };
const disk = { kind: "host" as const, name: "web-01", part: "/" };

describe("formatDays", () => {
  it.each([
    [9.2, "9 days"],
    [1.2, "1 day"],
    [0.3, "less than a day"],
    [365, "not filling"],
  ])("says %d as %s", (days, expected) => {
    expect(formatDays(days)).toBe(expected);
  });
});

describe("formatDiskShare", () => {
  it("rounds to whole percent but keeps a decimal just below full", () => {
    expect(formatDiskShare(0.861)).toBe("86%");
    expect(formatDiskShare(0.996)).toBe("99.6%");
    expect(formatDiskShare(1)).toBe("100%");
  });
});

describe("formatMeasure", () => {
  it("speaks each rule's unit", () => {
    expect(formatMeasure("error-rate", 0.062)).toBe("6.2%");
    expect(formatMeasure("latency-p95", 3.4)).toBe("3.4 s");
    expect(formatMeasure("disk-space", 0.91)).toBe("91%");
    expect(formatMeasure("disk-filling", 9)).toBe("9 days");
  });
});

describe("alertTitle", () => {
  it("names what is wrong while open", () => {
    expect(alertTitle("disk-filling", disk, 9.2, false)).toBe("Disk / on web-01 is full in 9 days");
    expect(alertTitle("disk-space", { ...disk, part: "/var/log" }, 0.91, false)).toBe(
      "Disk /var/log on web-01 is 91% full",
    );
    expect(alertTitle("latency-p95", service, 3.4, false)).toBe("checkout-web answers slowly: p95 3.4 s");
    expect(alertTitle("error-rate", { ...service, name: "payments" }, 0.216, false)).toBe("payments logs 21.6% errors");
  });

  it("names the worst it was once resolved", () => {
    expect(alertTitle("latency-p95", service, 3.4, true)).toBe("checkout-web answered slowly: p95 3.4 s");
    expect(alertTitle("disk-space", disk, 0.92, true)).toBe("Disk / on web-01 was 92% full");
    expect(alertTitle("disk-filling", disk, 9, true)).toBe("Disk / on web-01 was filling: full in 9 days");
    expect(alertTitle("error-rate", service, 0.012, true)).toBe("checkout-web logged 1.2% errors");
  });
});

describe("rule lines", () => {
  it("summarises each rule under an alert", () => {
    expect(ruleSummary("error-rate", 0.05, 300)).toBe("Error rate · above 5% for 5 min");
    expect(ruleSummary("latency-p95", 1, 300)).toBe("Latency · p95 above 1 s for 5 min");
    expect(ruleSummary("disk-space", 0.9, 300)).toBe("Disk space · above 90% in use");
    expect(ruleSummary("disk-filling", 14, 86400)).toBe("Disk filling · full within 14 days");
  });

  it("says each rule in a sentence", () => {
    expect(ruleSentence("disk-filling", "Caution", 14, 86400)).toBe(
      "Built-in rule: caution when a disk is full within 14 days at the pace of the last day.",
    );
    expect(ruleSentence("latency-p95", "Warning", 3, 300)).toBe(
      "Built-in rule: warning when the p95 of requests is 3 s or more over 5 minutes.",
    );
  });

  it("names the rules, subjects, and thresholds", () => {
    expect(ruleName("disk-filling")).toBe("Disk filling");
    expect(subjectLabel(disk)).toBe("web-01 · /");
    expect(subjectLabel(service)).toBe("checkout-web");
    expect(formatThreshold("disk-filling", 3)).toBe("within 3 days");
    expect(formatThreshold("latency-p95", 1)).toBe("1 s");
  });
});

describe("database rules", () => {
  const backup = { kind: "database" as const, name: "microsoft.sql_server sql-prod-01", part: "shop" };
  const server = { kind: "database" as const, name: "microsoft.sql_server sql-prod-01", part: null };

  it("names the server and its part in words", () => {
    expect(alertTitle("db-backup-age", backup, 31 * 3600, false)).toBe(
      "SQL Server sql-prod-01: shop has no full backup for 1 d 7 h",
    );
    expect(alertTitle("db-long-query", backup, 840, false)).toBe(
      "SQL Server sql-prod-01: a query in shop has run for 14 min",
    );
    expect(alertTitle("db-blocking", server, 3, false)).toBe("SQL Server sql-prod-01: 3 sessions are blocked");
    expect(alertTitle("db-deadlocks", server, 1, true)).toBe("SQL Server sql-prod-01: 1 deadlock in 15 min");
    expect(subjectLabel(backup)).toBe("SQL Server sql-prod-01 · shop");
  });

  it("measures and links each rule", () => {
    expect(formatMeasure("db-agent-job", 1)).toBe("failed");
    expect(formatMeasure("db-backup-age", 26 * 3600)).toBe("1 d 2 h");
    expect(ruleSummary("db-deadlocks", 1, 900)).toBe("Deadlocks · 1 deadlock or more in 15 min");
    expect(databasePath(server.name)).toBe("/metrics/databases/microsoft.sql_server/sql-prod-01");
    expect(serverName("postgresql pg-main:5432")).toBe("PostgreSQL pg-main:5432");
  });
});
