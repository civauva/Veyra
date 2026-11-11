# 0009. Alert notifications: channels, a delivery outbox, and the webhook payload

Date: 2026-11-10
Status: Accepted

## Context

v0.1 promises a minimal alerting workflow with email or webhook notifications (README roadmap). The built-in
error-rate rule already opens, changes, and resolves alerts every 30 seconds (VEY-17). People need to hear about
them where they work: in a chat tool or by email.

Constraints:

- Rule evaluation must never wait for a slow or broken receiver, and a failing receiver must never cost telemetry.
- Webhook URLs of Slack, Mattermost, and similar tools carry a token in the URL. Anyone who reads it can post into
  the channel.
- The SMTP server needs a password, and secrets never go in code, configuration files, or the database
  (AGENTS.md section 4).
- Receivers need a stable document to parse. Once integrations exist, changing it breaks them.

## Decision

Administrators manage **channels** in the UI; Veyra keeps them in PostgreSQL. A channel is a webhook or an email
list, switched on or off, and chooses warnings, cautions, and resolutions.

Every change of an alert is an **event**, and every event a channel wants becomes a **delivery** row, written in the
same transaction as the change: a transactional outbox. A separate dispatcher claims due deliveries with
`FOR UPDATE SKIP LOCKED`, sends them with a 10-second limit, and retries six times over about 25 minutes before
marking them failed.

Who hears what: opened and escalated alerts go to channels that take that severity; resolutions go only to channels
that were told about the alert; easing from warning to caution is not sent.

Webhook URLs are **encrypted with ASP.NET Data Protection**, with the key ring that already protects session
cookies, and the API returns only their start. Webhooks must use HTTPS, except to this machine, and redirects are not
followed. The SMTP server and its password come from environment variables only.

Webhooks receive one JSON document, `{ "event", "text", "alert" }`, described in
[`docs/architecture/alerting.md`](../architecture/alerting.md#what-a-webhook-receives). `text` is a readable line, so
Slack and Mattermost incoming webhooks show it as it is. New fields may be added; existing ones keep their name and
meaning until a new ADR changes them.

## Alternatives considered

- **Sending from the evaluator directly.** Simplest, but a receiver that hangs for 10 seconds would delay every
  rule, and a crash between saving the alert and sending would lose the message.
- **An in-memory queue.** No database rows, but messages are lost on restart, and nothing could show what was sent
  on the alert's timeline.
- **Channels in environment variables only.** Less to build, but every change means editing a file and restarting,
  and there is no place to test a channel or see that it fails.
- **Storing webhook URLs in plain text.** Simpler and survives a lost key ring, but every database backup would hold
  working tokens for the team's chat.
- **Formats per tool (Slack blocks, Teams cards).** Nicer messages, but one format per tool to maintain. A generic
  document with a `text` line covers the common tools now; tool-specific formats can be added later as a channel
  option.
- **`System.Net.Mail.SmtpClient`.** Part of .NET, but Microsoft does not recommend it for new code. MailKit is the
  usual choice and is maintained.

## Consequences

- Alert evaluation stays fast and independent of receivers; messages survive restarts and show on the alert's
  timeline with their state.
- Losing the `veyra-keys` volume now also makes stored webhook URLs unreadable; their deliveries fail with a message
  to enter the URL again. The deployment guide tells to back the volume up.
- The webhook document is a public contract. It needs care whenever alerts gain fields, and a version field if it
  ever has to change incompatibly.
- A message is sent at least once: a crash after the receiver accepted it but before Veyra recorded that can send
  it again after the lease runs out. Receivers should tolerate a duplicate.
- MailKit is a new runtime dependency of the Alerting module.
