import type { ChannelInput, NotificationChannel, SendResult } from "../api/types";
import { formatClock } from "../logs/format";

/*
 * Words of the Notifications screen. Times are UTC, like everywhere in Veyra.
 */

/**
 * The line under a channel's name: where it sends.
 *
 * @param channel - The channel.
 * @returns The webhook's hint or the email addresses.
 */
export function channelTarget(channel: NotificationChannel): string {
  return channel.kind === "Webhook"
    ? `Webhook · ${channel.webhookUrlHint ?? ""}`
    : `Email · ${(channel.emailRecipients ?? []).join(", ")}`;
}

/**
 * The summary under a channel's activity bars.
 *
 * @param channel - The channel.
 * @returns For example `14 days · 23 sent, 0 failed`.
 */
export function activitySummary(channel: NotificationChannel): string {
  const { sent, failed, days } = channel.activity;

  return `${days.length} days · ${sent} sent, ${failed} failed`;
}

/**
 * The most recent try, for the right of the summary.
 *
 * @param channel - The channel.
 * @returns For example `Last 07:01 · 204`, `Next try 07:12`, or null when it never sent.
 */
export function lastTry(channel: NotificationChannel): string | null {
  const { lastAttemptAt, lastError, lastStatus, nextAttemptAt } = channel.activity;

  if (lastError && nextAttemptAt) {
    return `Next try ${formatClock(nextAttemptAt)}`;
  }

  if (!lastAttemptAt) {
    return null;
  }

  const result = lastError ? "failed" : lastStatus === null ? "sent" : String(lastStatus);

  return `Last ${formatClock(lastAttemptAt)} · ${result}`;
}

/**
 * The result of a test message, as a short sentence.
 *
 * @param result - How the test went.
 * @returns For example `Test delivered (204).` or the reason it failed.
 */
export function testOutcome(result: SendResult): string {
  if (result.succeeded) {
    return result.status === null ? "Test sent." : `Test delivered (${result.status}).`;
  }

  return `Test failed: ${result.error ?? "unknown error"}.`;
}

/**
 * Splits what was typed into the recipients box: one address per line, or separated by commas or semicolons.
 *
 * @param text - The box's text.
 * @returns The addresses, trimmed, without empty entries.
 */
export function parseRecipients(text: string): string[] {
  return text
    .split(/[\n,;]+/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

/**
 * The input that changes only whether a channel is switched on, keeping everything else, the stored URL included.
 *
 * @param channel - The channel.
 * @param enabled - Whether it should be on.
 * @returns The input for `PUT /api/v1/notification-channels/{id}`.
 */
export function toggledInput(channel: NotificationChannel, enabled: boolean): ChannelInput {
  return {
    name: channel.name,
    kind: channel.kind,
    enabled,
    sendWarning: channel.sendWarning,
    sendCaution: channel.sendCaution,
    sendResolved: channel.sendResolved,
    webhookUrl: null,
    emailRecipients: channel.emailRecipients,
  };
}

/**
 * The JSON a webhook receives, filled with a sample alert, for the preview beside the form.
 *
 * @param publicHost - The address Veyra links to, shown in the sample.
 * @returns The JSON, indented.
 */
export function samplePayload(publicHost: string): string {
  return JSON.stringify(
    {
      event: "escalated",
      text: "Now warning: payments-gateway logs 6.2% errors (production)",
      alert: {
        id: "0193a2b4-…",
        rule: "error-rate",
        severity: "warning",
        service: "payments-gateway",
        environment: "production",
        value: 0.062,
        threshold: 0.05,
        peak: 0.062,
        windowSeconds: 300,
        openedAt: "2026-11-10T06:58:00+00:00",
        resolvedAt: null,
        url: `${publicHost}/alerts/0193a2b4-…`,
      },
    },
    null,
    2,
  );
}
