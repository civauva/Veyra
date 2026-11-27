import { describe, expect, it } from "vitest";

import { addTerm, quoteValue, removeTerm, replaceTerm, termText } from "./queryText";

describe("the text of a log query (ADR 0024)", () => {
  it("quoteValue_PlainValue_StaysAsItIs", () => {
    expect(quoteValue("orders-api")).toBe("orders-api");
  });

  it("quoteValue_SpacesOrQuotes_AreQuoted", () => {
    expect(quoteValue("Orders API")).toBe('"Orders API"');
    expect(quoteValue('say "hi"')).toBe('"say \\"hi\\""');
    expect(quoteValue("")).toBe('""');
  });

  it("termText_EachField_WritesTheLanguage", () => {
    expect(termText("service", "Orders API")).toBe('service:"Orders API"');
    expect(termText("level", "warning", ">=")).toBe("level>=warning");
    expect(termText("attribute", "/pay", ":", "http.route")).toBe("http.route:/pay");
    expect(termText("text", "timed out")).toBe('"timed out"');
  });

  it("addTerm_EmptyQuery_IsTheTerm", () => {
    expect(addTerm("  ", "host:web-01")).toBe("host:web-01");
  });

  it("addTerm_QueryWithTerms_JoinsWithAnd", () => {
    expect(addTerm("service:orders ", "host:web-01")).toBe("service:orders AND host:web-01");
  });

  it("removeTerm_MiddleTerm_TakesItsAndWithIt", () => {
    const text = 'service:orders AND level>=warning AND "timed out"';

    expect(removeTerm(text, { start: 19, end: 33 })).toBe('service:orders AND "timed out"');
  });

  it("removeTerm_FirstTerm_TakesTheAndAfterIt", () => {
    expect(removeTerm("service:orders AND host:web-01", { start: 0, end: 14 })).toBe("host:web-01");
  });

  it("removeTerm_OnlyTerm_LeavesNothing", () => {
    expect(removeTerm("host:web-01", { start: 0, end: 11 })).toBe("");
  });

  it("removeTerm_TermsJoinedBySpaces_KeepsTheOthers", () => {
    expect(removeTerm("service:orders host:web-01 timeout", { start: 15, end: 26 })).toBe("service:orders timeout");
  });

  it("replaceTerm_Term_PutsTheNewOneInItsPlace", () => {
    expect(replaceTerm("service:orders AND host:web-01", { start: 0, end: 14 }, "service:billing")).toBe(
      "service:billing AND host:web-01",
    );
  });
});
