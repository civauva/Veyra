import { describe, expect, it } from "vitest";

import { alertFixPath, rulePath } from "./rulePath";

describe("rulePath", () => {
  it("rulePath_NoSubject_IsTheRuleAlone", () => {
    expect(rulePath("error-rate")).toBe("/alerts/rules/error-rate");
  });

  it("rulePath_Subject_NamesItAndItsPart", () => {
    expect(
      rulePath("db-log-backup-age", { kind: "database", name: "microsoft.sql_server sql-prod-01", part: "shop" }),
    ).toBe("/alerts/rules/db-log-backup-age?kind=database&name=microsoft.sql_server+sql-prod-01&part=shop");
  });

  it("alertFixPath_Alert_OpensAtTheFix", () => {
    expect(alertFixPath("a1")).toBe("/alerts/a1#fix");
  });
});
