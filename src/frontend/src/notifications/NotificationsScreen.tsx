import { useCallback, useEffect, useState } from "react";

import { api } from "../api/client";
import type { EmailServer, NotificationChannel } from "../api/types";
import { ChannelCard } from "./ChannelCard";
import { ChannelEditor } from "./ChannelEditor";
import "./notifications.css";

/** The message of anything thrown. */
function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}

/** What is open in the side panel: nothing, a new channel, or one being changed. */
type Editing = { channel: NotificationChannel | null } | null;

/**
 * Where Veyra tells people about alerts, for administrators: the email server from the settings, and one card per
 * webhook or email channel. Channels are added and changed in a side panel.
 */
export function NotificationsScreen() {
  const [list, setList] = useState<{ emailServer: EmailServer; channels: NotificationChannel[] } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing>(null);

  const reload = useCallback(() => {
    api
      .listChannels()
      .then((next) => {
        setList(next);
        setLoadError(null);
      })
      .catch((failure: unknown) => setLoadError(messageOf(failure)));
  }, []);

  useEffect(reload, [reload]);

  const close = useCallback(() => setEditing(null), []);

  return (
    <main className="notifications">
      <div className="notifications-head">
        <div>
          <h1>Notifications</h1>
          <p>
            Where Veyra tells people about alerts: when one opens, gets worse, or clears. Each channel chooses which
            severities it wants.
          </p>
        </div>
        <button type="button" className="button primary" onClick={() => setEditing({ channel: null })}>
          + Add channel
        </button>
      </div>

      {list && <EmailServerStrip server={list.emailServer} />}

      {loadError && (
        <p className="notifications-error" role="alert">
          {loadError}
        </p>
      )}

      {list && list.channels.length === 0 && (
        <p className="notifications-empty">
          No channels yet. Add a webhook for your team&apos;s chat, or an email for the people on call. Until then,
          alerts show only on the home.
        </p>
      )}

      {list && list.channels.length > 0 && (
        <div className="channel-cards">
          {list.channels.map((channel) => (
            <ChannelCard
              key={channel.id}
              channel={channel}
              emailServer={list.emailServer}
              onEdit={() => setEditing({ channel })}
              onChanged={reload}
            />
          ))}
        </div>
      )}

      {editing && (
        <ChannelEditor
          channel={editing.channel}
          onClose={close}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </main>
  );
}

/** How each SMTP security setting reads on the screen. */
const securityWords: Record<EmailServer["security"], string> = {
  Auto: "TLS when offered",
  StartTls: "STARTTLS",
  SslOnConnect: "TLS",
  None: "no encryption",
};

/** The SMTP server email channels send through, which comes from the environment, not from this screen. */
function EmailServerStrip({ server }: { server: EmailServer }) {
  return (
    <p className={server.configured ? "email-server" : "email-server missing"}>
      <i aria-hidden="true" />
      {server.configured ? (
        <span>
          <b>Email server</b> · <code>{`${server.host ?? ""}:${server.port}`}</code> · {securityWords[server.security]}{" "}
          · from <code>{server.from}</code>
        </span>
      ) : (
        <span>
          <b>No email server</b> · email channels cannot send until <code>VEYRA_SMTP_HOST</code> and{" "}
          <code>VEYRA_SMTP_FROM</code> are set
        </span>
      )}
      <span className="grow" />
      <span className="email-server-where">
        Set in <code>deploy/.env</code>
      </span>
    </p>
  );
}
