import { describe, expect, it } from "vitest";

import { avatarColor, eventText, formatSince, formatWhen, initialsOf, outcomeText } from "./userFormat";
import { newPassword } from "./passwords";

const now = new Date("2026-11-10T08:00:00Z");

describe("initialsOf", () => {
  it("takes the first letters of the first two names", () => {
    expect(initialsOf("Lena Hoffmann")).toBe("LH");
    expect(initialsOf("mei")).toBe("M");
    expect(initialsOf("Ana Maria Silva")).toBe("AM");
  });
});

describe("avatarColor", () => {
  it("gives the same person the same colour every time", () => {
    expect(avatarColor("01a1c25f")).toBe(avatarColor("01a1c25f"));
  });
});

describe("formatWhen", () => {
  it("says today and yesterday with the time in UTC, and days ago after that", () => {
    expect(formatWhen("2026-11-10T07:41:00Z", now)).toBe("Today 07:41");
    expect(formatWhen("2026-11-09T18:02:00Z", now)).toBe("Yesterday 18:02");
    expect(formatWhen("2026-11-07T09:00:00Z", now)).toBe("3 days ago");
    expect(formatWhen("2026-09-14T09:00:00Z", now)).toBe("Sep 14");
    expect(formatWhen(null, now)).toBe("Never");
  });
});

describe("formatSince", () => {
  it("reads inside a sentence: lower-case today and yesterday, a date after that", () => {
    expect(formatSince("2026-11-10T07:41:00Z", now)).toBe("today 07:41");
    expect(formatSince("2026-11-09T08:15:00Z", now)).toBe("yesterday 08:15");
    expect(formatSince("2026-10-12T09:00:00Z", now)).toBe("Oct 12");
  });
});

describe("outcomeText", () => {
  it("names how a sign-in ended and whether it was bad", () => {
    expect(outcomeText("SignedInWithCode")).toEqual({ text: "Signed in with a code", tone: "ok" });
    expect(outcomeText("SignedInWithRecoveryCode")).toEqual({ text: "Signed in with a recovery code", tone: "warn" });
    expect(outcomeText("WrongPassword")).toEqual({ text: "Wrong password", tone: "bad" });
  });
});

describe("eventText", () => {
  it("reads each change as a sentence", () => {
    expect(eventText({ at: "", kind: "RoleChanged", by: "Dana Kim", detail: "Viewer Editor" })).toBe(
      "Role Viewer → Editor",
    );
    expect(eventText({ at: "", kind: "TeamChanged", by: null, detail: "" })).toBe("Team removed");
    expect(eventText({ at: "", kind: "Added", by: "Dana Kim", detail: "Viewer" })).toBe("Added as Viewer");
    expect(eventText({ at: "", kind: "SessionsEnded", by: "Tom", detail: "2" })).toBe("2 sessions signed out");
  });
});

describe("newPassword", () => {
  it("makes four random words, long enough for the password rule", () => {
    const first = newPassword();
    const second = newPassword();

    expect(first.split("-")).toHaveLength(4);
    expect(first.length).toBeGreaterThanOrEqual(12);
    expect(first).not.toBe(second);
  });
});
