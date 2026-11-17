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
| `PUT /api/v1/account/time-zone` | Viewer | Keeps the time zone your screens show times in, or clears it |
| `GET /api/v1/logs` | Viewer | Searches log records |
| `GET /api/v1/logs/summary` | Viewer | Counts records over time and per service |
| `GET /api/v1/traces` | Viewer | Lists traces by their root spans, newest first, a page at a time |
| `GET /api/v1/traces/durations` | Viewer | The durations of the newest traces, for the list's chart |
| `GET /api/v1/traces/services` | Viewer | The services that received requests in the window |
| `GET /api/v1/traces/{traceId}` | Viewer | One trace with every span that has arrived |
| `GET /api/v1/metrics/overview` | Viewer | Every host, service, database server, and container that sent metrics, and metric storage use |
| `GET /api/v1/metrics/hosts/{host}` | Viewer | One host: CPU, memory, disks, network, disk I/O, load, containers |
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
| `GET /api/v1/settings` | Admin | Every setting with its value, default, and who saved it |
| `POST /api/v1/settings/review` | Admin | Checks changes and says what they will do; saves nothing |
| `PUT /api/v1/settings` | Admin | Saves changes, all or nothing, from a known version |
| `GET /api/v1/settings/history` | Admin | Changes, newest first |
| `POST /api/v1/settings/history/{id}/revert` | Admin | Sets a setting back to its value before a change |
| `GET /api/v1/settings/export` | Admin | Saved values as a JSON file, without secrets |
| `POST /api/v1/settings/import` | Admin | Reviews what a settings file would change |
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
| `GET /api/v1/agents` | Admin | Veyra's agents, the built-in one first, each with its state |
| `POST /api/v1/agents` | Admin | Adds an agent for another network; the answer holds its one-time code, once |
| `POST /api/v1/agents/{id}/code` | Admin | A new one-time code for an agent in another network |
| `PUT /api/v1/agents/{id}/host` | Admin | Sets what an installed agent reads of its own host |
| `DELETE /api/v1/agents/{id}` | Admin | Removes an agent in another network and revokes its key |
| `GET /api/v1/sql-servers` | Admin | The SQL Servers agents read, each with its state in words |
| `POST /api/v1/sql-servers` | Admin | Adds a SQL Server; its agent reads it within 30 seconds |
| `PUT /api/v1/sql-servers/{id}` | Admin | Changes a SQL Server; without `password` the stored one stays |
| `DELETE /api/v1/sql-servers/{id}` | Admin | Removes a SQL Server; the data it sent stays until retention |
| `GET /api/v1/postgres-servers` | Admin | The PostgreSQL servers agents read, each with its state in words |
| `POST /api/v1/postgres-servers` | Admin | Adds a PostgreSQL server; its agent reads it within 30 seconds |
| `PUT /api/v1/postgres-servers/{id}` | Admin | Changes a PostgreSQL server; without `password` the stored one stays |
| `DELETE /api/v1/postgres-servers/{id}` | Admin | Removes a PostgreSQL server; the data it sent stays until retention |

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
- Buckets are aligned to the clock, in UTC: the first starts with the window and ends at the first round time, the
  others start on whole multiples of the width. So there can be one bucket more than the window divided by the width.
  Counting reads counts kept per minute and hour (ADR 0015), so it takes the same time for an hour as for 14 days.
- `errors` counts records at `Error` or worse.
- `services` lists the 50 busiest services, most first. It ignores the `service` filter, so the UI can show what
  else there is to pick.

## Traces: `/api/v1/traces`

Traces arrive over OTLP (ADR 0014) and are read here. Every endpoint takes the same window and filters:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `from`, `to` | the last hour | The window, at most 31 days, on each trace's start |
| `service` | all | Only traces whose request came to this service; repeat for several |
| `environment` | all | Only traces of this environment; repeat for several |
| `failed` | `false` | Only traces whose request failed: the root span has error status |
| `minDurationMs`, `maxDurationMs` | none | Only traces that took at least, or at most, this long |
| `name` | none | Only traces whose name contains this, ignoring case |
| `attribute` | none | `key:value` the root span must have; repeat for several |

### `GET /api/v1/traces`

Also takes `slow=true` (only traces at least as slow as the 95th percentile of `/durations`), `limit` (1 to
`Veyra:Query:MaxPageSize`, default 50), and `cursor` from the previous page.

```json
{
  "items": [{ "traceId": "4bf92f3577b34da6a3ce929d0e0e4736", "rootSpanId": "00f067aa0ba902b7",
              "name": "POST /api/checkout", "service": "checkout-web", "environment": "production",
              "start": "2026-11-09T12:00:00Z", "durationMs": 1840.2, "failed": true, "spans": 27, "failedSpans": 4,
              "cause": { "service": "psp-client", "spanName": "POST /authorize",
                         "message": "The PSP did not answer within 1.5 s" },
              "services": [{ "service": "payments-gateway", "spans": 12 }] }],
  "nextCursor": "MTc5NDIyNTYwMDAwMDAwMDA6MDBmMDY3YWEwYmE5MDJiNw"
}
```

- A trace is listed from its root span, so a trace whose root span has not arrived, or was not sampled, is not
  listed; it is still reached by its ID.
- `spans`, `failedSpans`, `cause`, and `services` are counted from the trace's other spans when the page is read.
  `cause` is the deepest failed span, preferring one with an exception: the cause rather than the requests that passed
  the failure on. Its `message` is the exception message, else the status message, else the span name.
- Pages by keyset: the 20th page costs what the first does.

### `GET /api/v1/traces/durations`

```json
{ "points": [{ "traceId": "4bf9…", "start": "2026-11-09T12:00:00Z", "durationMs": 1840.2, "failed": true }],
  "read": 20000, "maxRead": 20000, "p95Ms": 420.5 }
```

Reads at most the newest 20,000 traces of the window, so it costs the same however busy the window was; `read` says
how many were read. `points` holds every failed trace, every trace at least as slow as `p95Ms`, and an even sample
of the rest, at most 1,500, oldest first.

### `GET /api/v1/traces/services`

`[{ "service": "checkout-web", "traces": 695 }]`: the services among the newest 20,000 traces of the window, most
traces first. A `service` filter is ignored, so the UI can show what else there is to pick.

### `GET /api/v1/traces/{traceId}`

Every span that has arrived, by start time. `at`, when the trace is known to have happened (from a log record or the
list), makes Veyra read that day and the days next to it first; without it every day kept is read. `400` when the ID
is not 32 hexadecimal characters, `404` when no span of it is stored.

```json
{ "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spans": [{ "spanId": "00f067aa0ba902b7", "parentSpanId": null, "name": "POST /api/checkout", "kind": "Server",
              "start": "2026-11-09T12:00:00Z", "durationMs": 1840.2, "status": "Error", "statusMessage": "502",
              "service": "checkout-web", "environment": "production", "host": "web-02",
              "attributes": { "http.route": "/api/checkout" }, "resourceAttributes": { "service.name": "checkout-web" },
              "events": [{ "timestamp": "2026-11-09T12:00:01.7Z", "name": "exception",
                           "attributes": { "exception.type": "System.TimeoutException" } }],
              "links": [], "dropped": { "attributes": 0, "events": 0, "links": 0 } }] }
```

The log records of a trace come from `GET /api/v1/logs?traceId=…`.

## The home snapshot: `GET /api/v1/home`

Everything a home dashboard draws, in one answer (ADR 0007). The UI asks again every 15 seconds. The snapshot of an
environment is built at most once every 5 seconds and shared by everyone who asks (ADR 0015), so `at` can be up to
5 seconds old.

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
                 "latency": { "label": "p95", "latest": 0.34, "points": [] } }],
  "databases": [{ "engine": "microsoft.sql_server", "server": "sql-prod-01", "environment": "production",
                  "lastSeen": "2026-11-10T11:00:12Z", "cpus": 8, "memoryBytes": 55834574848,
                  "uptimeSeconds": 3542400, "batchRequests": 1240, "connections": 182, "blocked": 0, "deadlocks": 2,
                  "pageLifeSeconds": 3412, "longestQuerySeconds": 134, "databases": 5,
                  "oldestBackupSeconds": 111600, "failedJobs": 1,
                  "requests": { "label": "batch requests", "latest": 1240, "points": [] } }],
  "containers": [{ "name": "shop-worker", "host": "web-01", "environment": "production",
                   "image": "ghcr.io/example/shop-worker:1.4.2", "id": "9b81c0de55f2",
                   "lastSeen": "2026-11-10T11:00:10Z", "cpuPercent": 148, "memoryBytes": 499122176,
                   "memoryLimitBytes": 536870912, "memoryRatio": 0.93, "restarts": 3, "restartsTotal": 7,
                   "uptimeSeconds": 240, "cpu": { "label": "cpu", "latest": 140, "points": [] } }]
}
```

- `hosts` are the hosts that sent `hostmetrics` in the last 15 minutes, by `host.name`. `fillingDisk` is the disk
  that fills up soonest within 30 days, by a straight line through the last day of usage, or `null`. Every disk has
  `daysUntilFull`, `null` when it is not filling up or would take more than a year.
- `services` are the services that sent HTTP server or runtime metrics in the last 15 minutes, per environment.
  `serverErrorRatio` is the share of requests answered with a 5xx status.
- `databases` are the database servers that sent in the last 15 minutes (ADR 0018), by `db.system.name` and
  `server.address`. `deadlocks` counts the last 15 minutes; `oldestBackupSeconds` is the longest any database has
  gone without a full backup; `failedJobs` counts SQL Agent jobs whose last run failed. A PostgreSQL server
  (`engine` `postgresql`, VEY-33) has no `cpus`, `memoryBytes`, `uptimeSeconds`, `batchRequests`, or
  `pageLifeSeconds`; its `connections` are those to every database, and it adds `maxConnections` (its
  `max_connections`), `commitsPerSecond` and `rollbacksPerSecond` now, and `wraparoundRatio`, the highest
  transaction ID age of any database of the limit of 2^31, from 0 to 1. Its `requests` line is transactions
  committed or rolled back per second. These four fields are `null` for SQL Server.
- `containers` are the Docker containers that sent `docker_stats` in the last 15 minutes (ADR 0018), by
  `container.name` and then `host`. `cpuPercent` is a share of one CPU, so 148 is about one and a half CPUs, and the
  `cpu` line has the same unit. `memoryRatio` is `memoryBytes` (without the page cache) of `memoryLimitBytes`;
  Docker reports the host's memory as the limit of a container without one. The three memory fields are `null` while
  the container is between restarts and reports a limit of 0. `restarts` counts the restarts within
  the range, `null` when the container sends no restart count; `restartsTotal` is Docker's own count. `id` is the
  short container ID.
- The `cpu` and `latency` lines have at most 72 points: `bucketSeconds` is 60 for `1h`, 300 for `6h`, 1800 for
  `24h`, and 10800 for `7d`. The host and service screens draw their charts with the finer buckets of the range.

### `GET /api/v1/metrics/hosts/{host}` and `/services/{service}`

One host or service in detail, with its card as on the overview. A service takes `environment`; leave it out for a
service that names none. Both answer `404` when nothing arrived in the range.

- **Host.** `cpu` (total, user, system, iowait as shares), `memory` (used, cache as shares), `disks`, `network`
  and `diskIo` (bytes per second, by direction), `load` (1m, 5m, 15m), and `containers`: the Docker containers on
  the host that sent within the range, by name, each as on the overview, with `restarts` counted over the range.
- **Service.** `latency` (p50, p95, p99 in seconds), `requests` (ok, 4xx, 5xx per second), `routes` (the busiest
  ten in the last five minutes, with `requestsPerSecond`, `p95Seconds`, and `serverErrorRatio`), `resources` (cpu
  share, memory bytes), `runtime` (garbage collections per minute, thread pool queue, threads), and
  `instanceNames`.

### `GET /api/v1/metrics/databases/{engine}/{server}`

One database server in detail (ADR 0018), for example `/api/v1/metrics/databases/microsoft.sql_server/sql-prod-01`,
with its card as on the overview. `404` when nothing arrived in the range.

- **Lines.** `requests` (batch requests per second), `connections`, `blocked`, `deadlocks` (per point),
  `pageLife` (seconds), `longestQuery` (seconds, of any database), and `waits`: seconds waited per second, one
  line per wait category, the six largest, without the waits of idle sessions such as `SLEEP_*` and `WAITFOR`.
- **`databases`**, the largest first: `sizeBytes` (data and log), `logBytes`, `growthBytesPerDay` over the last
  day, `backupAgeSeconds` (`null` when never backed up), `ioLatencySeconds` (the average file read or write
  over the range), and `longestQuerySeconds`.
- **`jobs`**, the failed first: `failed`, `lastRun`, `durationSeconds`.
- **`topQueries`**: the latest report of the queries with the most total time, with `totalSeconds`, `executions`,
  `workerSeconds`, and `logicalReads`.
- **PostgreSQL** (`/api/v1/metrics/databases/postgresql/pg-main`, VEY-33) reads the tested PostgreSQL configuration
  instead: `requests` is transactions per second, `connections` the connections to every database, and `blocked`
  the sessions waiting for a lock longer than 30 seconds; `pageLife`, `waits`, `jobs`, `running`, and `topQueries`
  are empty. It adds the lines `maxConnections`, `commits`, and `rollbacks` (per second), which are `null` for SQL
  Server. Each of `databases` adds `connections`, `transactionIdAge`, `wraparoundRatio` (that age of 2^31, from 0
  to 1), `liveRows` and `deadRows` of all its tables, `deadRowsRatio` (dead of all rows), and `vacuums` (of its
  tables over the last day); `logBytes`, `backupAgeSeconds`, and `ioLatencySeconds` are `null`. **`tables`** lists
  the ten tables with the most dead rows, the most first, each with `database`, `schema`, `name`, `liveRows`,
  `deadRows`, `deadRowsRatio`, and `vacuums` over the last day; it is empty for SQL Server.

### `GET /api/v1/metrics/databases/{engine}/{server}/live`

The server's Live tab (ADR 0020): its newest read of busy sessions, or with `at` (an ISO 8601 time) the read at or
before that moment, within two minutes of it. `404` when the server sent neither reads nor metrics.

- **`sampledAt`**, **`ageSeconds`**: when the read was made and how old it is; `null` without a read.
- **`tiles`**: batch requests per second with its line over 15 minutes, `blocked` with its line,
  `longestRequestSeconds`, `oldestTransactionSeconds` and its session, `grantsWaiting`, `tempdbFreeRatio`,
  `tempdbSizeBytes`.
- **`findings`**, the worst first: `kind`, `severity` (`ok`, `caution`, `warning`), `title` and `detail` in words for
  anyone, `advanced` in SQL Server's terms, the `session` it is about, and `actions` (`copy` a statement such as
  `KILL 88;`, a program's `logs`, a `session`'s panel).
- **`blocking`**: the chains, each `session` with how many are `waiting` behind it and its `children`.
- **`sessions`**: every busy session with its status, wait, blocker, transaction, tempdb, grant, hashes, `name`
  (what the statement does, in words), and `text`.
- **`waits`**: every wait type the sessions wait on, with its `category`, `simple` words, `meaning`, and `causes`.

### `GET /api/v1/metrics/databases/{engine}/{server}/queries`, `/queries/{queryHash}`, `/plans/{planHash}`

The Queries tab and a statement's page (ADR 0020). `queries?range=` lists up to 100 statements by total time with
`name`, `text`, `database`, `totalSeconds`, `executions`, `cpuSeconds`, `reads`, `perRunSeconds`, `plans`, and
`findings` (`kind`, `severity`, `title`, `detail`, `fix`, `script`); Veyra's agent is counted apart.
`queries/{queryHash}?range=` adds `points` per bucket with the plan that ran most, `plans` with their per-run
figures, `operators`, and `warnings`, and `callers` from the live view; `404` when it did not run in the range.
The tab also lists `queryStore`: each user database, whether it keeps Query Store, and the statement that turns it on;
the page adds `history`, its Query Store intervals, and `waits` by category.
`plans/{planHash}` returns a captured plan as a `.sqlplan` file; `404` when Veyra holds none.

### `GET /api/v1/metrics/databases/{engine}/{server}/blocking`

The Blocking tab over `range` (ADR 0020): `episodes`, the newest first, each with `start`, `end`, the `head`
blocker's session, `program`, `host`, whether it was `sleeping`, its statement, `mostWaiting`, `longestWaitSeconds`,
`waitingPrograms`, and a `summary` sentence; and `deadlocks`, each with `at`, a `summary`, its `processes` (session,
program, host, login, database, `victim`, the lock it waited for, statement) and `resources` (holders and waiters).

### `GET /api/v1/metrics/databases/{engine}/{server}/storage`

Each database in full (ADR 0020): state, recovery model, settings, log use and virtual log files, `files`, backups
with the log chain, the last full backup's duration, size, and destination, the last good CHECKDB, damaged pages, and
`findings` with the statement to fix each; the `volumes` with their free space and `daysUntilFull`; and server-wide
`findings`. `404` before the server's first storage read.

### `GET /api/v1/metrics/databases/{engine}/{server}/indexes`

Index and statistics advice (ADR 0020): `findings` with the statement to run, and the 100 largest `indexes` with
their keys, size, reads, writes, and lock escalations since SQL Server started.

### `GET /api/v1/metrics/databases/{engine}/{server}/server`

The server as a whole over `range` (ADR 0020): version, update level, edition, uptime, whether it is out of support,
CPU of SQL Server and of others now and as lines, runnable tasks, workers, memory, grants waiting, dumps of the last
day, SQL Server Agent, batch requests now and usual for this hour of the week, the 15 wait types with the most time
explained, and `findings`. `404` when it sent nothing in the range.

### `GET /api/v1/metrics/databases/{engine}/{server}/jobs`

The server's SQL Agent jobs (ADR 0020), those that need a look first: `failed`, `lastRun`, `lastSeconds`,
`runningSeconds`, `nextRun`, `late`, `usualSeconds`, and the `runs` of the last week with their `steps` and messages.

### `GET /api/v1/metrics/databases/{engine}/{server}/events`

The server's `system_health` events over `range` (ADR 0020), the newest first: `at`, `name`, `severity`, `words` for
anyone, `error`, `errorSeverity`, `wait`, and `message`.

### `GET /api/v1/metrics/databases/{engine}/{server}/availability`

The server's Availability Group `replicas` (role, healthy), `databases` (queue, lag, synchronizing), `logShipping`
(side, behind, delay), and cluster `nodes` (state, owner) (ADR 0020); empty lists for a server without them.

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
| `days` | `30` | How many days back, today counting as one; 1 to `Veyra:Alerting:HistoryDays` (90) |
| `timeZone` | UTC | The viewer's IANA zone, such as `Europe/Berlin`, whose midnight starts each day; `400` when unknown |
| `state` | `all` | `open`, `resolved`, or `all` |
| `rule` | all | Only these rules; repeat for several: `error-rate`, `latency-p95`, `disk-space`, `disk-filling`, `container-restarts`, `container-memory`, `db-blocking`, `db-deadlocks`, `db-long-query`, `db-backup-age`, `db-agent-job`, `db-connections`, `db-wraparound` |
| `severity` | all | Only alerts whose worst severity was `Warning`, or `Caution` |
| `environment` | all | Only alerts of this environment |
| `kind` | all | Only alerts about a `service`, a `host`, a `database` server, or a `container` |
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

## Catalog rules: `GET /api/v1/alerts/catalog`

The catalog rules (ADR 0020) with what screens need to word their alerts: `id`, `name`, `description`, `unit`
(`seconds`, `ratio`, `count`, `bytes`, `days`, `flag`), `noun`, `headline` and `recovery` with `{server}`, `{part}`,
and `{value}` to fill in, `partLabel`, `lowerIsWorse`, and the installation's `enabled`, `caution`, and `warning`.

## Alert counts: `GET /api/v1/alerts/summary`

The Alerts screen's counts and weather log over the same period as the list.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `days` | `30` | How many days back, as for the list |
| `timeZone` | UTC | The viewer's IANA zone, as for the list |
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
- `days` has one entry per day in `timeZone`, oldest first: the alerts that opened that local day and the worst of
  them. Alerts open before the period count on its first day. `from` is midnight in that zone, as an instant.

## Time zone: `PUT /api/v1/account/time-zone`

The zone every screen shows the signed-in person's times in (VEY-22), kept on the account so it follows them to every
device. `GET /api/v1/session` and `GET /api/v1/account` return it as `timeZone`; `null` means none was picked, and
the browser's zone is used.

```json
{ "timeZone": "Europe/Berlin" }
```

- Names are IANA names, as browsers report them; Windows names are refused. `null` clears the choice.
- `204` when kept, `400` with a problem when the name is not a known zone.
- Every time in the API stays an ISO 8601 instant in UTC; only days that the API groups, such as the alert summary's,
  follow a `timeZone` parameter.

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

## Settings: `/api/v1/settings`

Administrators only. Every module's settings, saved in the database and applied without a restart
([ADR 0017](../adr/0017-settings-in-the-admin-ui.md)). Values travel in configuration form: whole numbers and numbers
as text, a ratio from `0` to `1`, a duration as `d.hh:mm:ss`, a switch as `true` or `false`.

`GET /api/v1/settings` answers with the `version` to save from, every setting, and a `system` list of what is set where
Veyra is installed. A setting holds:

| Field | Meaning |
| --- | --- |
| `key` | The configuration key, such as `Veyra:Storage:LogRetentionDays` |
| `section`, `group`, `label`, `help` | Where the screen shows it, and what it does |
| `kind` | `WholeNumber`, `Number`, `Ratio`, `Duration`, `Switch`, `Text`, `Choice`, or `Secret` |
| `unit`, `minimum`, `maximum`, `choices` | How it is shown and its limits, in the unit shown |
| `effect` | `None`, `DeletesData`, or `MayLockOut` |
| `advanced` | Whether the screen shows it only in its Advanced view |
| `value`, `isSet` | The value in effect; a secret's value is always `null` and only `isSet` says it has one |
| `default`, `defaultSource` | The value without a saved one, and where it comes from: `code`, `file`, `environment` |
| `saved`, `savedBy`, `savedAt` | Whether a value is saved on the screen, by whom, and when |

`POST /api/v1/settings/review` takes `{ "version": 7, "changes": [{ "key": "…", "value": "3" }] }`; a `null` value
resets the setting to its default. It answers with `problems` (with the `key` to fix), `notes` (what each change will
do, `risky` when it deletes data or may lock people out), the `changes` from and to, and `confirmationRequired`.

`PUT /api/v1/settings` takes the same body with `confirmed` and an optional `origin`, the file name of an import. It
answers with the settings after the save; `400` with the review when it has problems or needs a confirmation that
was not given; `409` when another save came first.

`GET /api/v1/settings/history` takes `section`, `changedBy`, `from`, `before` (the `next` of the previous page), and
`limit` (at most 200). A secret's values are never in it. `POST /api/v1/settings/history/{id}/revert` takes
`{ "version": 8, "confirmed": false }` and answers like a save.

The history also lists who added, changed, or removed a SQL Server, a PostgreSQL server, or an agent (ADR 0019),
under the sections `Databases` and `Agents`. Such a change has `subject` (`SqlServer`, `PostgresServer`, or `Agent`),
`label` set to its name, `kind` `Added`, `Changed`, or `Removed`, and `fields`: the fields a change touched, such as
`["port", "password"]`, never their values. It cannot be reverted, and it does not move the settings `version`, so a
save started before it still goes through.

`GET /api/v1/settings/export` answers with `{ "format": "veyra-settings", "formatVersion": 1, "exportedAt": …,
"values": { … } }`, without secrets. `POST /api/v1/settings/import` takes that file and answers with a review of
what it would change; the screen then saves it with `PUT` and the file name as `origin`.

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

## Agents and database servers: `/api/v1/agents`, `/api/v1/sql-servers`, `/api/v1/postgres-servers`

Administrators only. Veyra's agents read the database servers added here (ADR 0019); see
[`docs/integrations/sqlserver.md`](../integrations/sqlserver.md#from-the-settings-screen) and
[`docs/integrations/postgres.md`](../integrations/postgres.md#from-the-settings-screen).

A SQL Server, as `POST` and `PUT` take it:

```json
{
  "name": "sql-prod-01",
  "address": "sql-prod-01.corp.local",
  "port": 1433,
  "environment": "production",
  "agentId": null,
  "login": "veyra_monitor",
  "password": "…",
  "encryption": "Required"
}
```

- `name` is the server's database block: letters, digits, dots, dashes, and underscores, at most 63, unique. A
  taken name answers `409`; a broken rule answers `400` with the reason as the problem's title.
- `agentId` null means the built-in agent. `encryption` is `Required`, `TrustServerCertificate`, or `Off`.
- The password is stored encrypted and never answered. `PUT` with `"password": null` keeps the stored one.
- Each server in an answer has `state` and `detail`: `Receiving`, `WaitingForData`, `LoginFailed`, `Unreachable`,
  `CertificateRejected`, `GrantMissing`, `Failing`, `ConfigurationFailed`, `AgentOffline`, or `AgentNotConnected`,
  with the agent's or SQL Server's own words for an error, and `since`, when that error or the last data was seen.
- A PostgreSQL server (VEY-33) takes and answers the same fields, without `platform` and `database`. Its `login` is
  the role the agent signs in as: letters, digits, underscores, dots, and dashes, at most 63. Its password may not
  hold spaces, quotes, or backslashes, which the Collector's `postgresql` receiver cannot pass on. `encryption` is
  `Required` (TLS, checking the certificate and the server's name), `TrustServerCertificate` (TLS, any certificate),
  or `Off` (no TLS). Names are unique among PostgreSQL servers; its states are the same as a SQL Server's, with
  PostgreSQL's own words in `detail`.
- Each agent has `placement` (`BuiltIn` or `Remote`), `state` (`Online`, `NotConnected`, `Offline`,
  `ConfigurationFailed`, or `Unhealthy`), the Collector's `version`, `hostName`, `operatingSystem`, `lastSeenAt`,
  `error`, and how many database `servers` it reads, SQL Server and PostgreSQL together.

- `POST /api/v1/agents` takes `{ "name": "Company network" }` and answers `{ "agent", "code", "expiresAt" }`. The
  code works once, for an hour; only its hash is kept. An agent in another network has `installed` false until its
  installer used a code, and `codeExpiresAt` while a code is open. Removing an agent that still reads database servers
  answers `409`; removing the built-in one answers `400`.
- `PUT /api/v1/agents/{id}/host` takes `{ "host", "eventLog", "iis", "docker", "environment" }` and answers the
  agent, which shows the same fields, the environment as `hostEnvironment`. `eventLog` and `iis` are for Windows
  agents, `docker` for Linux ones ([`docker.md`](../integrations/docker.md)); asking for them elsewhere, for the
  built-in agent, or for one not installed answers `400`. Turning `host` off turns the others off.

Agents themselves talk to Veyra over OpAMP, at `POST /v1/opamp`, signed with their own ingestion key. It is not part
of the REST API; it follows the [OpAMP specification](https://opentelemetry.io/docs/specs/opamp/) over plain HTTP.
Installers in other networks use `GET /v1/agents/install.ps1` and `/v1/agents/install.sh`, and exchange a code at
`POST /v1/agents/enroll` with `{ "code", "platform": "Windows" | "Linux" }`, which answers the agent's start-up file as
`text/yaml`, or `403` for a wrong, used, or expired code. See [`agents.md`](../integrations/agents.md).

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
