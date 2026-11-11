import type { ChannelDay, EmailServer, NotificationChannel } from "../api/types";

/*
 * Channels for tests and the e2e run, matching the VEY-18 mockups: a busy Slack webhook, an on-call email, a failing
 * incident bot, and a paused weekend list.
 */

/** Fourteen days ending on 10 November 2026, with the given sent and failed counts per day. */
function days(sent: number[], failed: number[] = []): ChannelDay[] {
  return Array.from({ length: 14 }, (_, index) => ({
    day: new Date(Date.UTC(2026, 10, 10 - 13 + index)).toISOString().slice(0, 10),
    sent: sent[index] ?? 0,
    failed: failed[index] ?? 0,
  }));
}

const base = {
  createdAt: "2026-11-01T08:00:00Z",
  updatedAt: "2026-11-01T08:00:00Z",
  sendWarning: true,
  sendCaution: true,
  sendResolved: true,
  enabled: true,
};

/** A busy Slack webhook. */
export const opsAlerts: NotificationChannel = {
  ...base,
  id: "c1",
  name: "#ops-alerts",
  kind: "Webhook",
  webhookUrlHint: "https://hooks.slack.com/services/T0…",
  emailRecipients: null,
  activity: {
    days: days([3, 2, 0, 0, 4, 3, 0, 2, 0, 0, 5, 4, 0, 0]),
    sent: 23,
    failed: 0,
    lastAttemptAt: "2026-11-10T07:01:01Z",
    lastError: null,
    lastStatus: 204,
    nextAttemptAt: null,
  },
};

/** A webhook that keeps timing out. */
export const incidentBot: NotificationChannel = {
  ...base,
  id: "c3",
  name: "Incident bot",
  kind: "Webhook",
  sendCaution: false,
  sendResolved: false,
  webhookUrlHint: "https://bot.internal.example/veyra",
  emailRecipients: null,
  activity: {
    days: days([1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3]),
    sent: 2,
    failed: 3,
    lastAttemptAt: "2026-11-10T07:04:12Z",
    lastError: "Timed out after 10 s",
    lastStatus: null,
    nextAttemptAt: "2026-11-10T07:12:00Z",
  },
};

/** The on-call email. */
export const onCall: NotificationChannel = {
  ...base,
  id: "c2",
  name: "On-call",
  kind: "Email",
  sendCaution: false,
  webhookUrlHint: null,
  emailRecipients: ["oncall@example.com", "lead@example.com"],
  activity: {
    days: days([0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 2, 3, 0, 0]),
    sent: 6,
    failed: 0,
    lastAttemptAt: "2026-11-10T07:01:02Z",
    lastError: null,
    lastStatus: null,
    nextAttemptAt: null,
  },
};

/** A paused email list. */
export const weekendTeam: NotificationChannel = {
  ...base,
  id: "c4",
  name: "Weekend team",
  kind: "Email",
  enabled: false,
  sendCaution: false,
  webhookUrlHint: null,
  emailRecipients: ["weekend@example.com"],
  activity: {
    days: days([]),
    sent: 0,
    failed: 0,
    lastAttemptAt: null,
    lastError: null,
    lastStatus: null,
    nextAttemptAt: null,
  },
};

/** The four channels of the mockup, in the order the API lists them. */
export const channels: NotificationChannel[] = [opsAlerts, incidentBot, onCall, weekendTeam];

/** An SMTP server that is set up. */
export const emailServer: EmailServer = {
  configured: true,
  host: "smtp.example.com",
  port: 587,
  security: "StartTls",
  from: "veyra@example.com",
};
