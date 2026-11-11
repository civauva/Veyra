import { describe, expect, it } from "vitest";

import type { AlertDelivery, AlertEvent } from "../api/types";
import {
  alertScene,
  alertStory,
  deliveryNote,
  deliveryTag,
  eventTitle,
  formatDuration,
  headline,
  stateLine,
} from "./alertFormat";
import { openWarning, resolvedCaution } from "./testAlerts";

const sent: AlertDelivery = {
  channelId: "c1",
  channelName: "#ops-alerts",
  channelKind: "Webhook",
  state: "Sent",
  attempts: 1,
  lastAttemptAt: "2026-11-10T07:01:02Z",
  lastStatus: 204,
  lastError: null,
  nextAttemptAt: null,
  sentAt: "2026-11-10T07:01:02Z",
};

describe("formatDuration", () => {
  it("reads minutes, hours, and days", () => {
    expect(formatDuration(36 * 60_000)).toBe("36 min");
    expect(formatDuration(20_000)).toBe("1 min");
    expect(formatDuration(96 * 60_000)).toBe("1 h 36 min");
    expect(formatDuration(120 * 60_000)).toBe("2 h");
    expect(formatDuration((51 * 60 + 5) * 60_000)).toBe("2 d 3 h");
  });
});

describe("headline", () => {
  it("names the service and its error rate while open", () => {
    expect(headline(openWarning)).toBe("payments-gateway logs 21.6% errors");
  });

  it("says the service is back once resolved", () => {
    expect(headline(resolvedCaution)).toBe("orders-api is back to 0.4% errors");
  });
});

describe("stateLine", () => {
  it("says open, the severity, and for how long", () => {
    expect(stateLine(openWarning, new Date("2026-11-10T07:34:00Z"))).toBe("Open · warning · 36 min");
  });

  it("says resolved, the last severity, and how long it lasted", () => {
    expect(stateLine(resolvedCaution, new Date())).toBe("Resolved · was caution · lasted 14 min");
  });
});

describe("alertStory", () => {
  it("tells when each threshold was crossed and when it clears", () => {
    expect(alertStory(openWarning)).toBe(
      "The error rate in production went above 1% at 06:58 and above 5% at 07:01. It eases to caution below 2.5% " +
        "and clears when it stays below 0.5% for 5 minutes.",
    );
  });

  it("tells when a resolved alert cleared", () => {
    expect(alertStory(resolvedCaution)).toBe("The error rate went above 1% at 03:10. It cleared at 03:24, below 0.5%.");
  });
});

describe("eventTitle", () => {
  it("names every change", () => {
    const change: AlertEvent = {
      kind: "Opened",
      severity: "Caution",
      value: 0.02,
      at: "2026-11-10T06:58:00Z",
      deliveries: [],
    };

    expect(eventTitle({ ...change, kind: "Opened", severity: "Caution" })).toBe("Opened as caution");
    expect(eventTitle({ ...change, kind: "Escalated", severity: "Warning" })).toBe("Rose to warning");
    expect(eventTitle({ ...change, kind: "Eased", severity: "Caution" })).toBe("Eased to caution");
    expect(eventTitle({ ...change, kind: "Resolved" })).toBe("Resolved");
  });
});

describe("deliveryTag and deliveryNote", () => {
  it("shows a delivered webhook with its status", () => {
    expect(deliveryTag(sent)).toEqual({ text: "delivered · 204", tone: "ok" });
  });

  it("shows a sent email without a status", () => {
    expect(deliveryTag({ ...sent, channelKind: "Email", lastStatus: null })).toEqual({ text: "sent", tone: "ok" });
  });

  it("shows a message waiting for its first try as queued", () => {
    expect(deliveryTag({ ...sent, state: "Pending", attempts: 0, sentAt: null })).toEqual({
      text: "queued",
      tone: "wait",
    });
  });

  it("shows a failing message with its next try", () => {
    const retrying: AlertDelivery = {
      ...sent,
      state: "Pending",
      attempts: 3,
      lastError: "Timed out after 10 s",
      nextAttemptAt: "2026-11-10T07:12:00Z",
      sentAt: null,
    };

    expect(deliveryTag(retrying)).toEqual({ text: "retrying", tone: "bad" });
    expect(deliveryNote(retrying)).toBe("Try 3 failed: Timed out after 10 s. Next try 07:12.");
  });

  it("shows a message given up on", () => {
    const failed: AlertDelivery = { ...sent, state: "Failed", attempts: 6, lastError: "HTTP 502", sentAt: null };

    expect(deliveryTag(failed)).toEqual({ text: "failed", tone: "bad" });
    expect(deliveryNote(failed)).toBe("Gave up after 6 tries: HTTP 502.");
  });
});

describe("alertScene", () => {
  it("paints a storm for an open warning, showers for a caution, and clear skies once resolved", () => {
    expect(alertScene(openWarning)).toBe("storm");
    expect(alertScene({ ...openWarning, severity: "Caution" })).toBe("showers");
    expect(alertScene(resolvedCaution)).toBe("clear");
  });
});
