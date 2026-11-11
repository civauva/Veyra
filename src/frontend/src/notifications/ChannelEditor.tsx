import { useEffect, useId, useRef, useState, type FormEvent } from "react";

import { api } from "../api/client";
import type { ChannelInput, ChannelKind, NotificationChannel } from "../api/types";
import { parseRecipients, samplePayload, testOutcome } from "./channelFormat";
import { ChannelIcon } from "./ChannelIcon";

/** Props for {@link ChannelEditor}. */
interface ChannelEditorProps {
  /** The channel to change, or null to add one. */
  channel: NotificationChannel | null;
  /**
   * Called after the channel was saved.
   *
   * @param saved - The stored channel.
   */
  onSaved(saved: NotificationChannel): void;
  /** Called when the person closes the panel without saving. */
  onClose(): void;
}

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/**
 * The side panel to add or change a channel: webhook or email, its name, where it sends, and which alerts it hears
 * of. It can send a test before saving. A stored webhook URL is never shown again; leaving the field empty keeps it.
 *
 * Escape closes the panel, and focus starts in the first field and returns to where it was when the panel closes.
 */
export function ChannelEditor({ channel, onSaved, onClose }: ChannelEditorProps) {
  const titleId = useId();
  const first = useRef<HTMLInputElement>(null);
  const [kind, setKind] = useState<ChannelKind>(channel?.kind ?? "Webhook");
  const [name, setName] = useState(channel?.name ?? "");
  const [url, setUrl] = useState("");
  const [recipients, setRecipients] = useState((channel?.emailRecipients ?? []).join("\n"));
  const [sendWarning, setSendWarning] = useState(channel?.sendWarning ?? true);
  const [sendCaution, setSendCaution] = useState(channel?.sendCaution ?? true);
  const [sendResolved, setSendResolved] = useState(channel?.sendResolved ?? true);
  const [enabled, setEnabled] = useState(channel?.enabled ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [test, setTest] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    const before = document.activeElement;
    first.current?.focus();

    return () => {
      if (before instanceof HTMLElement) {
        before.focus();
      }
    };
  }, []);

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", onKey);

    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  function input(): ChannelInput {
    return {
      name,
      kind,
      enabled,
      sendWarning,
      sendCaution,
      sendResolved,
      webhookUrl: kind === "Webhook" && url.trim() ? url.trim() : null,
      emailRecipients: kind === "Email" ? parseRecipients(recipients) : null,
    };
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      onSaved(channel ? await api.updateChannel(channel.id, input()) : await api.createChannel(input()));
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  async function sendTest() {
    setBusy(true);
    setError(null);
    setTest(null);

    try {
      const result = await api.testDraft(input(), channel?.id ?? null);
      setTest({ ok: result.succeeded, text: testOutcome(result) });
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  const keepsUrl = channel?.kind === "Webhook" && kind === "Webhook";

  return (
    <>
      <div className="drawer-shade" aria-hidden="true" onClick={onClose} />
      <aside className="drawer" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <header className="drawer-head">
          <h2 id={titleId}>{channel ? `Change ${channel.name}` : "Add a channel"}</h2>
          <p>Veyra posts JSON to a webhook, or sends a short email with a link to the alert.</p>
        </header>
        <form className="drawer-form" onSubmit={(event) => void save(event)}>
          <fieldset className="kind-picker" disabled={channel !== null}>
            <legend className="visually-hidden">Kind</legend>
            {(["Webhook", "Email"] as const).map((option) => (
              <label key={option} className={kind === option ? "on" : undefined}>
                <input
                  type="radio"
                  name="kind"
                  value={option}
                  checked={kind === option}
                  onChange={() => setKind(option)}
                />
                <ChannelIcon kind={option} size={18} />
                {option}
              </label>
            ))}
          </fieldset>

          <label className="field">
            <span>Name</span>
            <input
              ref={first}
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder={kind === "Webhook" ? "#ops-alerts" : "On-call"}
              maxLength={80}
              required
            />
          </label>

          {kind === "Webhook" ? (
            <div className="field">
              <label htmlFor={`${titleId}-url`}>URL</label>
              <input
                id={`${titleId}-url`}
                className="mono"
                type="url"
                inputMode="url"
                value={url}
                onChange={(event) => setUrl(event.target.value)}
                placeholder={keepsUrl ? (channel?.webhookUrlHint ?? "") : "https://hooks.slack.com/services/…"}
                required={!keepsUrl}
                autoComplete="off"
                spellCheck={false}
                aria-describedby={`${titleId}-url-help`}
              />
              <small id={`${titleId}-url-help`}>
                {keepsUrl
                  ? "Leave it empty to keep the stored URL. It is not shown again, because it usually holds a token."
                  : "Treated as a secret: after saving, only its start is shown. HTTPS only, except for localhost."}
              </small>
            </div>
          ) : (
            <div className="field">
              <label htmlFor={`${titleId}-recipients`}>Recipients</label>
              <textarea
                id={`${titleId}-recipients`}
                value={recipients}
                onChange={(event) => setRecipients(event.target.value)}
                rows={3}
                placeholder={"oncall@example.com\nlead@example.com"}
                required
                aria-describedby={`${titleId}-recipients-help`}
              />
              <small id={`${titleId}-recipients-help`}>
                One address per line, up to 20. Every address gets the same email.
              </small>
            </div>
          )}

          <fieldset className="field">
            <legend>Send</legend>
            <div className="send-pills">
              <label className={sendWarning ? "pill on warning" : "pill warning"}>
                <input
                  type="checkbox"
                  checked={sendWarning}
                  onChange={(event) => setSendWarning(event.target.checked)}
                />
                <i aria-hidden="true" />
                Warning
              </label>
              <label className={sendCaution ? "pill on caution" : "pill caution"}>
                <input
                  type="checkbox"
                  checked={sendCaution}
                  onChange={(event) => setSendCaution(event.target.checked)}
                />
                <i aria-hidden="true" />
                Caution
              </label>
              <label className={sendResolved ? "pill on resolved" : "pill resolved"}>
                <input
                  type="checkbox"
                  checked={sendResolved}
                  onChange={(event) => setSendResolved(event.target.checked)}
                />
                <i aria-hidden="true" />
                Resolved
              </label>
            </div>
            <small>Resolved goes only to channels that were told about the alert.</small>
          </fieldset>

          <div className="field switch-field">
            <input
              id={`${titleId}-enabled`}
              type="checkbox"
              checked={enabled}
              onChange={(event) => setEnabled(event.target.checked)}
              aria-describedby={`${titleId}-enabled-help`}
            />
            <label htmlFor={`${titleId}-enabled`}>Switched on</label>
            <small id={`${titleId}-enabled-help`}>A channel that is off receives nothing, not even later.</small>
          </div>

          {kind === "Webhook" && (
            <div className="field">
              <span>What Veyra sends</span>
              <pre className="payload">{samplePayload(window.location.origin)}</pre>
              <small>
                The <code>text</code> field makes it readable in Slack and Mattermost incoming webhooks as it is.
              </small>
            </div>
          )}

          {error && (
            <p className="drawer-error" role="alert">
              {error}
            </p>
          )}
          {test && (
            <p className={test.ok ? "drawer-test ok" : "drawer-test bad"} role="status">
              {test.text}
            </p>
          )}

          <div className="drawer-actions">
            <button type="button" className="button" disabled={busy} onClick={() => void sendTest()}>
              Send test
            </button>
            <span className="grow" />
            <button type="button" className="button ghost" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="button primary" disabled={busy}>
              {channel ? "Save changes" : "Save channel"}
            </button>
          </div>
        </form>
      </aside>
    </>
  );
}
