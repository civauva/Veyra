import { describe, expect, it } from "vitest";

import type { Setting } from "../api/types";
import { changeWords, describeValue, fromShown, toShown } from "./settingsFormat";

/** A setting with the given kind and unit; the rest does not matter here. */
function setting(kind: Setting["kind"], unit: string | null = null, extra: Partial<Setting> = {}): Setting {
  return {
    key: "Veyra:Test:Value",
    section: "Rules",
    group: "Error rate",
    label: "Caution from",
    help: "Share of a service's log records that are errors.",
    kind,
    unit,
    minimum: null,
    maximum: null,
    choices: [],
    effect: "None",
    optional: false,
    advanced: false,
    environmentVariable: null,
    value: null,
    isSet: true,
    default: null,
    defaultSource: { kind: "code", name: null },
    saved: false,
    savedBy: null,
    savedAt: null,
    ...extra,
  };
}

describe("toShown", () => {
  it("shows a ratio as a percentage, without float noise", () => {
    expect(toShown(setting("Ratio", "%"), "0.01")).toBe("1");
    expect(toShown(setting("Ratio", "%"), "0.055")).toBe("5.5");
    expect(toShown(setting("Ratio", "%"), "0.07")).toBe("7");
  });

  it("shows a duration in the setting's unit", () => {
    expect(toShown(setting("Duration", "min"), "00:05:00")).toBe("5");
    expect(toShown(setting("Duration", "h"), "1.00:00:00")).toBe("24");
    expect(toShown(setting("Duration", "s"), "00:00:30")).toBe("30");
    expect(toShown(setting("Duration", "h"), "08:30:00")).toBe("8.5");
  });

  it("leaves numbers and text as they are, and an empty value empty", () => {
    expect(toShown(setting("WholeNumber", "days"), "14")).toBe("14");
    expect(toShown(setting("Text"), "smtp.example.com")).toBe("smtp.example.com");
    expect(toShown(setting("Number", "GB"), null)).toBe("");
  });
});

describe("fromShown", () => {
  it("turns a percentage back into a ratio", () => {
    expect(fromShown(setting("Ratio", "%"), "1")).toBe("0.01");
    expect(fromShown(setting("Ratio", "%"), "5.5")).toBe("0.055");
  });

  it("turns a duration back into d.hh:mm:ss", () => {
    expect(fromShown(setting("Duration", "min"), "5")).toBe("00:05:00");
    expect(fromShown(setting("Duration", "h"), "24")).toBe("1.00:00:00");
    expect(fromShown(setting("Duration", "s"), "90")).toBe("00:01:30");
  });

  it("keeps what is not a number as typed, so the server can say what is wrong", () => {
    expect(fromShown(setting("Ratio", "%"), "soon")).toBe("soon");
    expect(fromShown(setting("Duration", "min"), "")).toBe("");
  });
});

describe("describeValue", () => {
  it("says a value with its unit, and a switch as on or off", () => {
    expect(describeValue(setting("Ratio", "%"), "0.05")).toBe("5 %");
    expect(describeValue(setting("Duration", "min"), "00:05:00")).toBe("5 min");
    expect(describeValue(setting("Switch"), "false")).toBe("off");
    expect(describeValue(setting("WholeNumber", "days"), "14")).toBe("14 days");
  });

  it("says none for an empty optional value, and hides secrets", () => {
    expect(describeValue(setting("Number", "GB", { optional: true }), null)).toBe("none");
    expect(describeValue(setting("Secret"), "x")).toBe("hidden");
  });
});

describe("changeWords", () => {
  it("changeWords_NewValue_SaysFromAndTo", () => {
    const days = setting("WholeNumber", "days", { group: "Logs", label: "Keep", value: "14", default: "7" });

    expect(changeWords(days, "30")).toBe("Logs, Keep: 14 days to 30 days");
  });

  it("changeWords_Reset_SaysTheDefault", () => {
    const days = setting("WholeNumber", "days", { group: "Logs", label: "Keep", value: "14", default: "7" });

    expect(changeWords(days, null)).toBe("Logs, Keep: 14 days to the default, 7 days");
  });

  it("changeWords_Secret_SaysOnlyThatItChanged", () => {
    expect(changeWords(setting("Secret", null, { group: "Email", label: "Password" }), "hunter2")).toBe(
      "Email, Password: changed",
    );
  });
});
