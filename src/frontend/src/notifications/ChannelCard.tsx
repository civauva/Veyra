import { useState } from "react";

import { api } from "../api/client";
import type { EmailServer, NotificationChannel } from "../api/types";
import { formatClock } from "../logs/format";
import { activitySummary, channelTarget, lastTry, testOutcome, toggledInput } from "./channelFormat";
import { ChannelIcon } from "./ChannelIcon";

/** Props for {@link ChannelCard}. */
interface ChannelCardProps {
  /** The channel. */
  channel: NotificationChannel;
  /** The email server, so an email channel can say when it cannot send. */
  emailServer: EmailServer;
  /** Called when the person wants to change it. */
  onEdit(): void;
  /** Called after it was switched on or off, or deleted, so the list is read again. */
  onChanged(): void;
}

/** Ends an error from the API with a full stop, so the next sentence can follow it. */
function sentence(text: string | null): string {
  const trimmed = (text ?? "").trim();

  return /[.!?]$/.test(trimmed) ? trimmed : `${trimmed}.`;
}

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/**
 * One channel: where it sends, what it hears of, what it did over 14 days, and whether its last try failed. A switch
 * turns it off without losing it; a test message checks it now.
 */
export function ChannelCard({ channel, emailServer, onEdit, onChanged }: ChannelCardProps) {
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<{ ok: boolean; text: string } | null>(null);
  const [confirming, setConfirming] = useState(false);
  const { activity } = channel;
  const failing = activity.lastError !== null;
  const noServer = channel.kind === "Email" && !emailServer.configured;
  const most = Math.max(1, ...activity.days.map((day) => day.sent + day.failed));
  const last = lastTry(channel);

  async function act(action: () => Promise<void>) {
    setBusy(true);
    setNote(null);

    try {
      await action();
    } catch (failure) {
      setNote({ ok: false, text: messageOf(failure) });
    } finally {
      setBusy(false);
    }
  }

  const classes = ["channel-card", failing ? "failing" : "", channel.enabled ? "" : "off"].filter(Boolean).join(" ");

  return (
    <article className={classes} aria-labelledby={`channel-${channel.id}`}>
      <div className="channel-top">
        <span className={`channel-icon ${channel.kind.toLowerCase()}`}>
          <ChannelIcon kind={channel.kind} />
        </span>
        <div className="channel-name">
          <h2 id={`channel-${channel.id}`}>{channel.name}</h2>
          <p className="channel-target" title={channelTarget(channel)}>
            {channelTarget(channel)}
          </p>
        </div>
        <button
          type="button"
          role="switch"
          className="switch"
          aria-checked={channel.enabled}
          aria-label={`${channel.name} switched on`}
          disabled={busy}
          onClick={() =>
            void act(async () => {
              await api.updateChannel(channel.id, toggledInput(channel, !channel.enabled));
              onChanged();
            })
          }
        />
      </div>

      <p className="send-line">
        <span>Sends</span>
        <span className={channel.sendWarning ? "pill on warning" : "pill warning"}>
          <i aria-hidden="true" />
          Warning
        </span>
        <span className={channel.sendCaution ? "pill on caution" : "pill caution"}>
          <i aria-hidden="true" />
          Caution
        </span>
        <span className={channel.sendResolved ? "pill on resolved" : "pill resolved"}>
          <i aria-hidden="true" />
          Resolved
        </span>
        <span className="visually-hidden">
          {[
            channel.sendWarning ? "warnings" : null,
            channel.sendCaution ? "cautions" : null,
            channel.sendResolved ? "resolutions" : null,
          ]
            .filter(Boolean)
            .join(", ")}
        </span>
      </p>

      {noServer && (
        <p className="channel-problem">
          No email server is set up, so this channel cannot send. Set <code>VEYRA_SMTP_HOST</code> and{" "}
          <code>VEYRA_SMTP_FROM</code> in <code>deploy/.env</code>.
        </p>
      )}
      {failing && activity.lastAttemptAt && (
        <p className="channel-problem">
          <b>Last try failed at {formatClock(activity.lastAttemptAt)}.</b> {sentence(activity.lastError)}
          {activity.nextAttemptAt
            ? ` Veyra tries again at ${formatClock(activity.nextAttemptAt)}.`
            : " Veyra gave up on that message."}
        </p>
      )}
      {!channel.enabled && <p className="channel-off">Switched off. Nothing is sent while it is off.</p>}

      <div className="activity">
        <ol className="activity-bars" aria-label={`${activitySummary(channel)}, per day`}>
          {activity.days.map((day) => (
            <li key={day.day} title={`${day.day}: ${day.sent} sent, ${day.failed} failed`}>
              {day.sent + day.failed === 0 ? (
                <i className="empty" />
              ) : (
                <>
                  {day.failed > 0 && <i className="failed" style={{ height: `${(day.failed / most) * 100}%` }} />}
                  {day.sent > 0 && <i className="sent" style={{ height: `${(day.sent / most) * 100}%` }} />}
                </>
              )}
            </li>
          ))}
        </ol>
        <p className="activity-line">
          <span>{activitySummary(channel)}</span>
          {last && <b>{last}</b>}
        </p>
      </div>

      {note && (
        <p className={note.ok ? "channel-note ok" : "channel-note bad"} role="status">
          {note.text}
        </p>
      )}

      <div className="channel-actions">
        <button
          type="button"
          className="button small"
          disabled={busy}
          onClick={() =>
            void act(async () => {
              const result = await api.testChannel(channel.id);
              setNote({ ok: result.succeeded, text: testOutcome(result) });
            })
          }
        >
          Send test
        </button>
        <button type="button" className="button small ghost" onClick={onEdit}>
          Edit
        </button>
        <span className="grow" />
        {confirming ? (
          <span className="channel-confirm">
            Delete it and its history?
            <button
              type="button"
              className="button small danger"
              disabled={busy}
              onClick={() =>
                void act(async () => {
                  await api.deleteChannel(channel.id);
                  onChanged();
                })
              }
            >
              Yes, delete
            </button>
            <button type="button" className="button small ghost" onClick={() => setConfirming(false)}>
              Keep
            </button>
          </span>
        ) : (
          <button type="button" className="button small ghost" onClick={() => setConfirming(true)}>
            Delete
          </button>
        )}
      </div>
    </article>
  );
}
