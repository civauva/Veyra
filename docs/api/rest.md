# REST API

The API the Veyra UI uses, and that scripts can use too. All paths start with `/api/v1`. JSON uses camelCase,
times are ISO 8601 in UTC, enums are strings, and errors are problem details (RFC 9457).

## Signing in

Everything except the endpoints marked **open** requires a session (ADR 0008):

1. `POST /api/v1/session` with `{"email", "password"}` sets the session cookie and answers `200` with the user.
2. An account with a second factor answers `202` with `{"next": "code"}` instead, and a short-lived cookie for the
   next step (ADR 0011). `POST /api/v1/session/second-factor` with `{"code"}`, six digits from the authenticator
   app or a recovery code, then sets the session cookie. When an Admin requires a second factor the account does not
   have, `next` is `setup`: `POST /api/v1/session/second-factor/setup` returns the secret, and
   `POST /api/v1/session/second-factor/setup/confirm` with a working code starts the session and returns the ten
   recovery codes once. The next-step cookie lasts five minutes.
3. `GET /api/v1/session/antiforgery` returns `{"token"}`.
4. Every request that changes something sends that token in the `X-Veyra-Antiforgery` header.

Wrong codes count toward the account lock like wrong passwords. Every sign-in is a session that the Account page and
the Users screen list and can end; an ended session is refused on its next request.

Sign-in needs HTTPS outside development, because the cookies are `Secure`. Over plain HTTP, `POST /api/v1/session`
and `GET /api/v1/session/antiforgery` answer `400` with a problem that says so, before the password is checked.

## Endpoints

| Method and path | Who | What |
| --- | --- | --- |
| `GET /api/v1/setup` | open | Whether first-run setup is still needed |
| `POST /api/v1/setup` | open | Creates the first administrator with the token from the log |
| `POST /api/v1/session` | open | Signs in: a session, or the next step |
| `POST /api/v1/session/second-factor` | next step | Signs in with a code from the app or a recovery code |
| `POST /api/v1/session/second-factor/setup` | next step | Starts setting up a required second factor |
| `POST /api/v1/session/second-factor/setup/confirm` | next step | Confirms it, starts the session, returns recovery codes |
| `GET /api/v1/session` | signed in | The current user |
| `DELETE /api/v1/session` | signed in | Signs out and ends the session |
| `GET /api/v1/session/antiforgery` | signed in | The antiforgery token |
| `PUT /api/v1/session/password` | signed in | Changes your own password; signs out every other browser |
| `GET /api/v1/account` | Viewer | Your account, second factor, sessions, and latest sign-ins |
| `POST /api/v1/account/second-factor` | Viewer | Starts setting up a second factor |
| `POST /api/v1/account/second-factor/confirm` | Viewer | Switches it on with a working code; returns recovery codes |
| `POST /api/v1/account/second-factor/recovery-codes` | Viewer | New recovery codes; needs the password |
| `POST /api/v1/account/second-factor/off` | Viewer | Switches the second factor off; needs the password |
| `DELETE /api/v1/account/sessions/{id}` | Viewer | Ends one of your sessions |
| `POST /api/v1/account/sessions/end-others` | Viewer | Ends every other session of yours |
| `GET /api/v1/logs` | Viewer | Searches log records |
| `GET /api/v1/logs/summary` | Viewer | Counts records over time and per service |
| `GET /api/v1/metrics/overview` | Viewer | Every host and service that sent metrics, and metric storage use |
| `GET /api/v1/metrics/hosts/{host}` | Viewer | One host: CPU, memory, disks, network, disk I/O, load |
| `GET /api/v1/metrics/services/{service}` | Viewer | One service: latency, requests, routes, CPU, memory, runtime |
| `GET /api/v1/metrics/names` | Viewer | Metric names seen in the range, for the metric browser |
| `GET /api/v1/metrics/chart` | Viewer | One metric as lines, optionally split by an attribute |
| `GET /api/v1/home` | Viewer | The home snapshot: weather, alerts, services, key metrics, hours, outlook |
| `GET /api/v1/alerts` | Viewer | The alerts of the last days, newest first, a page at a time, with filters |
| `GET /api/v1/alerts/summary` | Viewer | The counts of the last days: open, total, per rule, per day; the rules' thresholds |
| `GET /api/v1/alerts/{id}` | Viewer | One alert: timeline with notifications and the measure of its rule |
| `GET /api/v1/users` | Admin | Lists users with team, lock, second factor, and sessions |
| `GET /api/v1/users/{id}` | Admin | One user: sign-ins, sessions, and the history of the account |
| `POST /api/v1/users` | Admin | Creates a local user |
| `PATCH /api/v1/users/{id}` | Admin | Changes a user's role, team, or disabled flag |
| `POST /api/v1/users/{id}/password` | Admin | Sets a new password; lifts a lock and ends every session |
| `DELETE /api/v1/users/{id}/second-factor` | Admin | Removes a second factor, for a lost phone; ends every session |
| `DELETE /api/v1/users/{id}/sessions` | Admin | Signs a user out everywhere |
| `DELETE /api/v1/users/{id}/sessions/{sessionId}` | Admin | Ends one session |
| `GET /api/v1/users/second-factor-policy` | Admin | Who must use a second factor |
| `PUT /api/v1/users/second-factor-policy` | Admin | Sets it: `Nobody`, `Admins`, or `Everyone` |
| `GET /api/v1/ingestion-keys` | Admin | Lists ingestion keys, without secrets |
| `POST /api/v1/ingestion-keys` | Admin | Creates a key; the response holds the secret once |
| `DELETE /api/v1/ingestion-keys/{id}` | Admin | Revokes a key |
| `GET /api/v1/notification-channels` | Admin | The email server and every channel with 14 days of activity |
| `POST /api/v1/notification-channels` | Admin | Adds a channel |
| `PUT /api/v1/notification-channels/{id}` | Admin | Changes a channel; a webhook without `webhookUrl` keeps its URL |
| `DELETE /api/v1/notification-channels/{id}` | Admin | Deletes a channel and its delivery history |
| `POST /api/v1/notification-channels/{id}/test` | Admin | Sends a test message to a stored channel |
| `POST /api/v1/notification-channels/test` | Admin | Sends a test message to a channel as entered, before saving |

## Searching logs: `GET /api/v1/logs`

| Parameter | Default | Meaning |
| --- | --- | --- |
| `from`, `to` | the last hour | The time window, at most 31 days |
| `severity` | all | The lowest severity, for example `Warning` |
| `service` | all | Only these services; repeat for several |
| `text` | none | Words that must all appear in the message, ignoring case |
| `traceId` | none | Only records of this trace |
| `environment` | all | Only these environments; repeat for several |
| `attribute` | none | `key:value`, for example `http.route:/pay`; repeat to require several |
| `limit` | 100 | Page size, 1 to `Veyra:Query:MaxPageSize` (1,000) |
| `cursor` | none | `nextCursor` from the previous page |

The answer is `{"items": [...], "nextCursor": "..."}`, newest first. `nextCursor` is `null` on the last page.

## Counting logs: `GET /api/v1/logs/summary`

Takes the same filters as `GET /api/v1/logs`, without `limit` and `cursor`, and answers with the counts behind the
Log Explorer's histogram and service filter:

```json
{
  "bucketSeconds": 120,
  "buckets": [{ "start": "2026-11-09T06:34:00Z", "total": 118, "errors": 2 }],
  "services": [{ "name": "payments-gateway", "count": 4812 }]
}
```

- `buckets` covers the whole window, oldest first, with empty buckets included. The width is the first of 30 s,
  1, 2, 5, 10, 15, and 30 min, 1, 2, 6, and 12 h, or 1 day that gives at most 40 buckets.
- `errors` counts records at `Error` or worse.
- `services` lists the 50 busiest services, most first. It ignores the `service` filter, so the UI can show what
  else there is to pick.

## The home snapshot: `GET /api/v1/home`

Everything a home dashboard draws, in one answer (ADR 0007). The UI asks again every 15 seconds.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `environment` | all | One environment, for example `production`. Left out: all environments, including records without one |

```json
{
  "environment": "production",
  "at": "2026-11-07T07:34:12Z",
  "environments": [{ "name": "production", "condition": "Storm", "errorRatio": 0.058 }],
  "condition": { "kind": "Storm", "over": "Services", "errorRatio": 0.058 },
  "counts": { "warning": 1, "caution": 1, "ok": 6, "unknown": 0 },
  "alerts": [{ "n": 1, "id": "…", "block": "service:production/payments-gateway", "service": "payments-gateway",
               "kind": "service", "name": "payments-gateway", "part": null,
               "environment": "production", "rule": "error-rate", "severity": "Warning", "value": 0.216,
               "peak": 0.24, "threshold": 0.05, "windowSeconds": 300, "since": "2026-11-07T06:58:00Z",
               "resolvedAt": null }],
  "resolved": [],
  "blocks": [{ "id": "service:production/orders-api", "kind": "service", "name": "orders-api",
               "environment": "production", "health": "Ok", "alertIds": [], "records": 2400, "errors": 2,
               "errorRatio": 0.0008 }],
  "metrics": { "errorRatio": 0.058, "ingestPerSecond": 66.4, "records": 19920, "windowSeconds": 300,
               "latency": { "p95Seconds": 0.38, "slowestService": "checkout-web", "slowestP95Seconds": 0.48,
                            "services": 3 } },
  "disks": [{ "host": "web-01", "mountpoint": "/", "ratio": 0.81, "daysUntilFull": 23 }],
  "hourly": { "observed": [{ "start": "2026-11-07T07:00:00Z", "total": 10000, "errors": 580 }],
              "expected": [{ "start": "2026-11-07T08:00:00Z", "load": 1.1 }], "daysOfHistory": 3 },
  "storage": { "usedBytes": 1288490188, "budgetBytes": 21474836480, "dailyBytes": 1181116006,
               "retentionDays": 30, "daysUntilBudget": 17, "reachesBudget": true }
}
```

- `condition.kind` is `Clear` without open alerts, `Showers` with cautions only, and `Storm` with any warning.
  `over` is the group of alert 1; until groupings exist every service is in `Services` and every host in `Hosts`.
- `errorRatio`, `metrics`, and each block's `records` and `errors` cover the last five minutes. A ratio is `null`
  when there were no records.
- `alerts` are the open alerts, numbered from 1: warnings first, then the oldest first. `resolved` holds the alerts
  resolved in the last 24 hours, newest first, with `n` set to `null`. `kind`, `name`, and `part` say what an alert
  is about (ADR 0012): a `service`, or a `host` and the mount point of one of its disks. `service` is the service
  name for service alerts and `null` for host alerts. `value`, `peak`, and `threshold` are in the rule's unit; see
  the Alerts list below.
- `blocks` lists every service that logged in the last hour or has an open alert, by name, then every host with an
  open alert. A host's `records` and `errors` are 0. `health` comes only from open alerts: a service with many errors
  is `Ok` until a rule fires. `Unknown` is not used yet.
- `hourly.observed` holds the past 23 hours and the current one. `hourly.expected` holds the next six: `load` is
  the usual record count of that hour relative to an average hour, learned from up to seven days, and `null` until
  the hour was seen on two days.
- `metrics.latency` is the p95 of all HTTP server requests over the last five minutes, from
  `http.server.request.duration`, with the slowest service. It is `null` when no service sends that metric.
- `disks` lists every disk of a host that sent host metrics in the last 15 minutes, the one that fills up first at
  the top. `daysUntilFull` comes from a straight line through the last day of usage; it is `null` for a disk that is
  not filling up, or would take more than a year. The list is empty without host metrics.
- `storage.reachesBudget` is `null` without a budget or before the first full day. `false` means retention keeps
  storage under the budget; then `daysUntilBudget` is `null`.

## Metrics: `/api/v1/metrics`

The Metrics screens (ADR 0010). Every endpoint takes `range`: `1h`, `6h` (the default), `24h`, or `7d`. Lines have
one point per bucket of `bucketSeconds`: one minute for 1h, 5 minutes for 6h, 15 minutes for 24h, and one hour for
7d. A point's `value` is `null` where nothing arrived. "Now" numbers, such as `cpuRatio`, cover the last five
minutes.

### `GET /api/v1/metrics/overview`

```json
{
  "at": "2026-11-10T11:00:20Z", "range": "6h", "bucketSeconds": 300,
  "usage": { "series": 1284, "maxSeries": 50000, "pointsPerSecond": 38, "usedBytes": 432013312,
             "retentionDays": 30, "names": 412 },
  "hosts": [{ "name": "web-01", "environment": "production", "os": "Ubuntu 24.04", "cpus": 4,
              "memoryBytes": 17179869184, "lastSeen": "2026-11-10T11:00:08Z", "cpuRatio": 0.42,
              "memoryRatio": 0.68, "load": 1.6,
              "fullestDisk": { "mountpoint": "/", "device": "/dev/sda1", "usedBytes": 86973087744,
                               "totalBytes": 107374182400, "ratio": 0.81, "daysUntilFull": 23 },
              "fillingDisk": { "mountpoint": "/", "…": "…" },
              "cpu": { "label": "cpu", "latest": 0.42, "points": [{ "at": "2026-11-10T05:05:00Z", "value": 0.4 }] } }],
  "services": [{ "name": "payments-gateway", "environment": "production", "runtime": ".NET", "instances": 3,
                 "lastSeen": "2026-11-10T11:00:15Z", "requestsPerSecond": 118, "p95Seconds": 0.34,
                 "serverErrorRatio": 0.021, "cpuRatio": 0.38, "memoryBytes": 1288490188,
                 "latency": { "label": "p95", "latest": 0.34, "points": [] } }]
}
```

- `hosts` are the hosts that sent `hostmetrics` in the last 15 minutes, by `host.name`. `fillingDisk` is the disk
  that fills up soonest within 30 days, by a straight line through the last day of usage, or `null`. Every disk has
  `daysUntilFull`, `null` when it is not filling up or would take more than a year.
- `services` are the services that sent HTTP server or runtime metrics in the last 15 minutes, per environment.
  `serverErrorRatio` is the share of requests answered with a 5xx status.

### `GET /api/v1/metrics/hosts/{host}` and `/services/{service}`

One host or service in detail, with its card as on the overview. A service takes `environment`; leave it out for a
service that names none. Both answer `404` when nothing arrived in the range.

- **Host.** `cpu` (total, user, system, iowait as shares), `memory` (used, cache as shares), `disks`, `network`
  and `diskIo` (bytes per second, by direction), and `load` (1m, 5m, 15m).
- **Service.** `latency` (p50, p95, p99 in seconds), `requests` (ok, 4xx, 5xx per second), `routes` (the busiest
  ten in the last five minutes, with `requestsPerSecond`, `p95Seconds`, and `serverErrorRatio`), `resources` (cpu
  share, memory bytes), `runtime` (garbage collections per minute, thread pool queue, threads), and
  `instanceNames`.

### `GET /api/v1/metrics/names` and `/chart`

The metric browser. `names` takes `search` (part of the name, ignoring case) and answers each name with its `kind`
(`Gauge`, `Sum`, `Histogram`), `unit`, `description`, number of `series`, and the services or hosts that send it.

`chart` takes:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `name` | required | The metric name; `400` without it, `404` when no series has it in the range |
| `show` | by kind | `value`, `rate`, `p50`, `p95`, or `p99`. Histograms default to `p95`, growing sums to `rate` |
| `split` | none | An attribute key, such as `http.route`, `service.name`, or `host.name`: one line per value |
| `service`, `host` | all | Only the series of this service or host |

The answer carries the `unit` (with `/s` for rates), `splitKeys` (every attribute key the series have, to split by),
and at most eight `lines`, the highest first.

## Alerts: `GET /api/v1/alerts`

The Alerts screen's list: every alert that was open at some time in the last days, newest first by the time it
opened, a page at a time (ADR 0012).

| Parameter | Default | Meaning |
| --- | --- | --- |
| `days` | `30` | How many UTC days back, today counting as one; 1 to `Veyra:Alerting:HistoryDays` (90) |
| `state` | `all` | `open`, `resolved`, or `all` |
| `rule` | all | Only these rules; repeat for several: `error-rate`, `latency-p95`, `disk-space`, `disk-filling` |
| `severity` | all | Only alerts whose worst severity was `Warning`, or `Caution` |
| `environment` | all | Only alerts of this environment |
| `kind` | all | Only alerts about a `service`, or a `host` |
| `q` | none | Only alerts whose service, host, or disk holds this text, in any case |
| `limit` | `50` | The most alerts on the page, 1 to 200 |
| `cursor` | none | The `next` of the previous page |

```json
{
  "items": [{ "id": "…", "rule": "disk-filling", "environment": "production",
              "block": { "kind": "host", "name": "web-01", "part": "/", "id": "host:production/web-01" },
              "severity": "Caution", "worst": "Caution", "value": 9.2, "peak": 9.2,
              "peakAt": "2026-11-10T07:30:00Z", "threshold": 14, "windowSeconds": 86400,
              "openedAt": "2026-11-10T03:20:00Z", "updatedAt": "2026-11-10T07:34:00Z", "resolvedAt": null }],
  "next": "MTc2Mjc0NDgwMDAwMDAwMDA6MDE5M2EyYjQ…"
}
```

- `value`, `peak`, and `threshold` are in the rule's unit: a ratio from 0 to 1 for `error-rate` and `disk-space`,
  seconds for `latency-p95`, and days until full for `disk-filling`, where 365 means not filling. `peak` is the worst
  measurement: the highest, or for days until full the fewest.
- `severity` is the severity now, or the last one of a resolved alert; `worst` the highest it ever had.
- `next` is `null` on the last page. An unknown rule, state, or kind, a limit or a number of days out of range, and a
  cursor Veyra did not make answer `400`.

## Alert counts: `GET /api/v1/alerts/summary`

The Alerts screen's counts and weather log over the same period as the list.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `days` | `30` | How many UTC days back, as for the list |
| `environment` | all | Only alerts of this environment |

```json
{
  "from": "2026-10-12T00:00:00+00:00", "to": "2026-11-10T07:34:00+00:00",
  "open": 2, "openWarnings": 1, "total": 23, "warnings": 5, "resolved": 21, "medianSeconds": 1080,
  "rules": [{ "rule": "error-rate", "count": 14, "enabled": true, "cautionThreshold": 0.01,
              "warningThreshold": 0.05, "windowSeconds": 300 }],
  "days": [{ "date": "2026-10-12", "count": 0, "worst": null }]
}
```

- `total` counts every alert open at some time in the period, `warnings` those that were at warning at some time,
  and `medianSeconds` is the median time from opening to resolving of those that resolved.
- `rules` lists every built-in rule, with this installation's thresholds in the rule's unit.
- `days` has one entry per UTC day, oldest first: the alerts that opened that day and the worst of them. Alerts
  open before the period count on its first day.

## One alert: `GET /api/v1/alerts/{id}`

The alert details screen and the link in every notification. Open alerts, and resolved ones for
`Veyra:Alerting:HistoryDays` (90 days); older or unknown IDs answer `404`.

```json
{
  "id": "0193a2b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b",
  "rule": "error-rate", "environment": "production", "service": "payments-gateway",
  "block": { "kind": "service", "name": "payments-gateway", "part": null,
             "id": "service:production/payments-gateway" },
  "severity": "Warning", "worst": "Warning", "value": 0.216, "peak": 0.241, "peakAt": "2026-11-10T07:12:00Z",
  "threshold": 0.05, "cautionThreshold": 0.01, "warningThreshold": 0.05, "clearThreshold": 0.005,
  "lowerIsWorse": false, "windowSeconds": 300, "minimumRecords": 20, "minimumRequests": null,
  "openedAt": "2026-11-10T06:58:00Z", "updatedAt": "2026-11-10T07:34:00Z", "resolvedAt": null,
  "events": [{ "kind": "Opened", "severity": "Caution", "value": 0.018, "at": "2026-11-10T06:58:00Z",
               "deliveries": [{ "channelId": "…", "channelName": "#ops-alerts", "channelKind": "Webhook",
                                "state": "Sent", "attempts": 1, "lastAttemptAt": "2026-11-10T06:58:01Z",
                                "lastStatus": 204, "lastError": null, "nextAttemptAt": null,
                                "sentAt": "2026-11-10T06:58:01Z" }] }],
  "series": { "bucketSeconds": 60, "from": "2026-11-10T06:28:00Z", "to": "2026-11-10T07:34:00Z",
              "points": [{ "at": "2026-11-10T06:29:00Z", "total": 610, "errors": 2, "windowTotal": 3050,
                           "windowErrors": 9, "ratio": 0.003 }] },
  "topErrors": [{ "message": "Timeout calling card-processor after 30000 ms", "count": 2104,
                  "first": "2026-11-10T06:58:12Z", "last": "2026-11-10T07:34:02Z" }],
  "disk": null,
  "latency": null
}
```

- Each rule fills its own part and leaves the others `null` (or `topErrors` empty): `series` and `topErrors` for
  `error-rate`, `latency` for `latency-p95`, and `disk` for `disk-space` and `disk-filling`.
- `clearThreshold` is where a caution of the rule ends: half the caution ratio for error rate, five points below it
  for disk space, 80 % of it for latency, and 1.5 times it for days until full. `lowerIsWorse` is `true` only for
  `disk-filling`.
- `latency` holds `points` per minute (wider for long alerts) from 30 minutes before the alert to 10 minutes after,
  each with `p50Seconds`, `p95Seconds`, and `requests`, and `routes` while the alert was open, slowest first, each
  with `method`, `route`, `requestsPerSecond`, `p95Seconds`, and `serverErrorRatio`.
- `disk` holds the disk's `mountpoint`, its `ratio`, `usedBytes`, `totalBytes`, and `daysUntilFull` now (`null` when
  the host no longer reports it), `points` of its share in use from a week before the alert opened, and every disk of
  the host now in `disks`, fullest first.

- `events` run oldest first. `kind` is `Opened`, `Escalated`, `Eased`, or `Resolved`; `severity` is the one after
  the change. Each event lists the messages it caused; `state` is `Pending`, `Sent`, or `Failed`, and
  `nextAttemptAt` is set while a failing message waits for its next try.
- `series` runs from 30 minutes before the alert opened to 10 minutes after it resolved, or to now. Each point's
  `ratio` is the error ratio over the rule's window ending at `at`, as the rule saw it; `null` without records.
  Points are one minute wide, wider for long alerts, so there are at most 240.
- `topErrors` are the five most frequent error messages, compared exactly, from the window that opened the alert
  until it resolved.

## Notification channels: `/api/v1/notification-channels`

Administrators only. See [`docs/architecture/alerting.md`](../architecture/alerting.md#notifications) for who is
told about what, retries, and the JSON a webhook receives.

A channel, as `POST` and `PUT` take it and as the test endpoint takes it:

```json
{
  "name": "#ops-alerts",
  "kind": "Webhook",
  "enabled": true,
  "sendWarning": true,
  "sendCaution": true,
  "sendResolved": true,
  "webhookUrl": "https://hooks.slack.com/services/…",
  "emailRecipients": null
}
```

- `kind` is `Webhook` or `Email`. A webhook needs `webhookUrl`, HTTPS except to localhost; an email needs 1 to 20
  bare addresses in `emailRecipients`. At least one of `sendWarning` and `sendCaution` must be true.
- Names are unique ignoring case; a taken name answers `409`. A broken rule answers `400` with the reason as the
  problem's title.
- Answers never contain the webhook URL, only `webhookUrlHint`, its start. To change a webhook without retyping its
  URL, send `"webhookUrl": null`.
- `GET` answers `{ "emailServer": { "configured", "host", "port", "security", "from" }, "channels": [...] }`. Each
  channel has `activity`: per UTC day of the last 14, messages sent and messages whose last try failed, and its
  most recent try with `lastError` and `nextAttemptAt`.
- The test endpoints send a message with `"event": "test"` right away and answer
  `{ "succeeded": false, "status": 404, "error": "HTTP 404 Not Found" }`: a failed send is a `200` with the reason.
  `POST …/test?id={id}` tests changes to a stored channel; a webhook without `webhookUrl` uses the stored URL.

## Users and your account: `/api/v1/users`, `/api/v1/account`

People, roles, second factors, sessions, and sign-in history (ADR 0008, ADR 0011).

```json
{
  "user": { "id": "…", "email": "tom.okafor@example.com", "displayName": "Tom Okafor", "role": "Editor",
            "disabled": false, "createdAt": "2026-06-23T09:00:00Z", "lastSignInAt": "2026-11-10T07:12:00Z",
            "team": "Payments", "lockedUntil": "2026-11-10T07:52:00Z", "secondFactorSince": "2026-10-03T10:00:00Z",
            "activeSessions": 2, "lastActiveAt": "2026-11-10T07:14:00Z" },
  "addedBy": "Dana Kim",
  "secondFactor": { "since": "2026-10-03T10:00:00Z", "recoveryCodesLeft": 8 },
  "secondFactorRequired": false,
  "sessions": [{ "id": "…", "browser": "Firefox on Ubuntu", "address": "198.51.100.7",
                 "startedAt": "2026-11-10T07:12:00Z", "lastActiveAt": "2026-11-10T07:14:00Z",
                 "method": "Code", "current": false }],
  "signIns": [{ "at": "2026-11-10T07:37:00Z", "outcome": "WrongPassword", "address": "203.0.113.48",
                "browser": "Chrome on Windows", "newAddress": true }],
  "signedInLast30Days": 19,
  "failedLast30Days": 5,
  "events": [{ "at": "2026-07-02T10:00:00Z", "kind": "RoleChanged", "by": "Dana Kim", "detail": "Viewer Editor" }]
}
```

- **`GET /api/v1/users/{id}`** above; the list has the `user` part of each person.
- **`outcome`** is `SignedIn`, `SignedInWithCode`, `SignedInWithRecoveryCode`, `WrongPassword`, `WrongCode`,
  `Locked`, or `SwitchedOff`. Attempts for emails that have no account are not stored.
- **`newAddress`** is true when the account had not signed in from that address within the kept history.
- **`kind`** is `Added`, `RoleChanged`, `SwitchedOff`, `SwitchedOn`, `PasswordChanged`, `PasswordReset`,
  `SecondFactorOn`, `SecondFactorOff`, `SecondFactorRemoved`, `RecoveryCodesReplaced`, `SessionsEnded`, or
  `TeamChanged`; `by` is `null` when Veyra itself made the change.
- **`method`** of a session is `Password`, `Code`, `RecoveryCode`, or `Provider`.
- **`GET /api/v1/account`** answers the same for yourself, with `hasPassword` and your latest 20 sign-ins;
  `current` marks the session making the request.
- **Kept.** Sign-in attempts and ended sessions for `Veyra:Access:SignInHistoryDays` (90 days); account changes as
  long as the account exists. Only Admins and the person see them.
- **Last Admin.** `PATCH` answers `409` when it would leave no active Admin. Switching your own second factor off
  answers `409` when an Admin requires it.
