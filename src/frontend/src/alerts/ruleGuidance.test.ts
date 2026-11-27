import { describe, expect, it } from "vitest";

import type { AlertDetails, AlertRule } from "../api/types";
import { catalogRuleIds, setRuleCatalog } from "./ruleCatalog";
import { ruleGuidance } from "./ruleGuidance";
import { integrityRule, openIntegrity, openWarning } from "./testAlerts";

/** An alert of a rule about a block, with the rest from a known alert. */
function alertOf(rule: AlertRule, kind: AlertDetails["block"]["kind"], name: string, part: string | null) {
  return { ...openWarning, rule, block: { kind, name, part, id: `${kind}:production/${name}` } };
}

const builtIn: [AlertRule, AlertDetails["block"]["kind"], string, string | null][] = [
  ["error-rate", "service", "payments-gateway", null],
  ["latency-p95", "service", "checkout-web", null],
  ["disk-space", "host", "web-01", "/"],
  ["disk-filling", "host", "web-01", "/var"],
  ["container-restarts", "container", "shop-api", "web-01"],
  ["container-memory", "container", "shop-api", "web-01"],
  ["db-blocking", "database", "microsoft.sql_server sql-prod-01", null],
  ["db-deadlocks", "database", "postgresql pg-01", null],
  ["db-long-query", "database", "microsoft.sql_server sql-prod-01", "Sales"],
  ["db-backup-age", "database", "microsoft.sql_server sql-prod-01", "Sales"],
  ["db-agent-job", "database", "microsoft.sql_server sql-prod-01", "Nightly backup"],
  ["db-connections", "database", "postgresql pg-01", null],
  ["db-wraparound", "database", "postgresql pg-01", "orders"],
];

describe("ruleGuidance", () => {
  it.each(builtIn)("says what %s means and how to fix it", (rule, kind, name, part) => {
    const guidance = ruleGuidance(alertOf(rule, kind, name, part));

    expect(guidance?.meaning).toMatch(/\w/);
    expect(guidance?.steps.length).toBeGreaterThan(0);
    expect(guidance?.steps.every((step) => step.text.length > 0)).toBe(true);
  });

  it("covers every built-in rule", () => {
    const named = new Set(builtIn.map(([rule]) => rule));
    const all: AlertRule[] = [
      "error-rate",
      "latency-p95",
      "disk-space",
      "disk-filling",
      "container-restarts",
      "container-memory",
      "db-blocking",
      "db-deadlocks",
      "db-long-query",
      "db-backup-age",
      "db-agent-job",
      "db-connections",
      "db-wraparound",
    ];

    expect(all.filter((rule) => !named.has(rule))).toEqual([]);
    expect(all.some((rule) => (catalogRuleIds as readonly string[]).includes(rule))).toBe(false);
  });

  it("gives the steps of the server's own engine", () => {
    const sqlServer = ruleGuidance(alertOf("db-blocking", "database", "microsoft.sql_server sql-prod-01", null));
    const postgres = ruleGuidance(alertOf("db-blocking", "database", "postgresql pg-01", null));

    expect(sqlServer?.steps.some((step) => step.script?.includes("sys.dm_exec_requests"))).toBe(true);
    expect(postgres?.steps.some((step) => step.script?.includes("pg_blocking_pids"))).toBe(true);
  });

  it("fills a catalog rule's guidance with the server and part, safe inside quotes and brackets", () => {
    setRuleCatalog([
      {
        ...integrityRule,
        meaning: "{server}: {part} is old.",
        steps: [{ text: "Check {part}.", script: "DBCC CHECKDB (N'{part}');\nALTER DATABASE [{part}] SET ONLINE;" }],
      },
    ]);
    const alert = { ...openIntegrity, block: { ...openIntegrity.block, part: "Bob's ]db" } };

    const guidance = ruleGuidance(alert);

    expect(guidance?.meaning).toBe("SQL Server PC-DRAZAN-2023: Bob's ]db is old.");
    expect(guidance?.steps[0]?.text).toBe("Check Bob's ]db.");
    expect(guidance?.steps[0]?.script).toBe("DBCC CHECKDB (N'Bob''s ]db');\nALTER DATABASE [Bob's ]]db] SET ONLINE;");
  });

  it("has no guidance for a catalog rule until the catalog has answered", () => {
    setRuleCatalog([]);

    expect(ruleGuidance(openIntegrity)).toBeNull();
  });
});
