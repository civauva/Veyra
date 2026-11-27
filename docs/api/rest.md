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
| `PUT /api/v1/account/look` | Viewer | Keeps the look of the app you picked, or clears it |
| `GET /api/v1/about` | Viewer | The running version and the build of the app it serves (ADR 0030) |
| `GET /api/v1/about/releases` | Viewer | The release notes, newest first |
| `GET /api/v1/views` | Viewer | Your saved views of a screen, and those others shared |
| `POST /api/v1/views` | Viewer | Saves a view of yours |
| `PUT /api/v1/views/{id}` | Viewer | Changes one of your views |
| `DELETE /api/v1/views/{id}` | Viewer | Deletes one of your views |
| `GET /api/v1/logs` | Viewer | Searches log records |
| `GET /api/v1/logs/summary` | Viewer | Counts records over time, per level, and per service |
| `GET /api/v1/logs/parse` | Viewer | The terms of a log query and their places in the text |
| `GET /api/v1/logs/export` | Viewer | Every matching record as an NDJSON or CSV file |
| `GET /api/v1/logs/stream` | Viewer | New log records as they are written, as server-sent events |
| `GET /api/v1/live` | Viewer | Events per second and a refresh tick, as server-sent events |
| `GET /api/v1/search` | Viewer | Services, hosts, databases, operations, and log records that match a text |
| `GET /api/v1/traces` | Viewer | Lists traces by their root spans, newest or worst first, a page at a time |
| `GET /api/v1/traces/durations` | Viewer | The durations of the newest traces, for the list's chart |
| `GET /api/v1/traces/services` | Viewer | The services that received requests in the window, with p95 and failures |
| `GET /api/v1/traces/summary` | Viewer | The figures of the window: traces, failed, slow, median, p95, services, period before |
| `GET /api/v1/traces/{traceId}` | Viewer | One trace with the spans that have arrived and the figures of all of them |
| `GET /api/v1/metrics/overview` | Viewer | Every host, service, database server, and container that sent metrics, and metric storage use |
| `GET /api/v1/metrics/hosts/{host}` | Viewer | One host: CPU, memory, disks, network, disk I/O, load, containers |
| `GET /api/v1/metrics/services/{service}` | Viewer | One service: latency, requests, routes, CPU, memory, runtime |
| `GET /api/v1/metrics/names` | Viewer | Metric names seen in the range, for the metric browser |
| `GET /api/v1/metrics/chart` | Viewer | One metric as lines, optionally split by an attribute |
| `GET /api/v1/resources/services` | Viewer | Every known service, worst first, a page at a time, with the header strip |
| `GET /api/v1/resources/hosts` | Viewer | Every known host and container, worst first, a page at a time, with the header strip |
| `GET /api/v1/resources/databases` | Viewer | Every known database server, worst first, a page at a time, with the header strip |
| `GET /api/v1/overview` | Viewer | The Console Overview in one read: figures, activity, ingestion, jobs, collectors, databases |
| `GET /api/v1/jobs` | Viewer | Every job from traces and SQL Server Agent, failing first, with its last run and run strip |
| `GET /api/v1/jobs/{id}` | Viewer | One job: its figures, durations, and runs of the window with steps, records, and retries |
| `GET /api/v1/home` | Viewer | The home snapshot: weather, alerts, services, key metrics, hours, outlook |
| `GET /api/v1/home/environments` | Viewer | The environment switcher's list for what its find box holds |
| `GET /api/v1/alerts` | Viewer | The alerts of the last days, newest first, a page at a time, with filters |
| `GET /api/v1/alerts/summary` | Viewer | The counts of the last days: open, total, per rule, per day; the rules' thresholds |
| `GET /api/v1/alerts/areas` | Viewer | How each area of a database server is from its open alerts, the worst first |
| `GET /api/v1/alerts/{id}` | Viewer | One alert: timeline with notifications and the measure of its rule |
| `GET /api/v1/alerts/{id}/context` | Viewer | What surrounds an alert: its block and related blocks, and their logs |
| `GET /api/v1/incidents` | Viewer | The incidents of the last days, open and acknowledged first, then newest |
| `GET /api/v1/incidents/{id}` | Viewer | One incident: its alerts, figures, timeline, and runbooks |
| `GET /api/v1/incidents/{id}/context` | Viewer | What surrounds an incident: failed path, exception, signals, charts, logs, components |
| `POST /api/v1/incidents/{id}/acknowledge` | Editor | Records that you are on the incident |
| `DELETE /api/v1/incidents/{id}/acknowledge` | Editor | Takes the acknowledgement back |
| `GET /api/v1/users` | Admin | Lists the users a search and a chip keep, with the screen's counts |
| `GET /api/v1/users/{id}` | Admin | One user: sign-ins, sessions, and the history of the account |
| `POST /api/v1/users` | Admin | Creates a local user |
| `PATCH /api/v1/users/{id}` | Admin | Changes a user's role, team, or disabled flag |
| `POST /api/v1/users/{id}/password` | Admin | Sets a new password; lifts a lock and ends every session |
| `DELETE /api/v1/users/{id}/second-factor` | Admin | Removes a second factor, for a lost phone; ends every session |
| `GET /api/v1/settings` | Admin | Every setting with its value, default, and who saved it |
| `GET /api/v1/settings/listing` | Admin | Which settings and sections the screen lists for a search, section, and view |
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
| `GET /api/v1/agents/summary` | Admin | How many servers and agents there are, those that need a look, and those online |
| `POST /api/v1/agents` | Admin | Adds an agent for another network; the answer holds its one-time code, once |
| `POST /api/v1/agents/{id}/code` | Admin | A new one-time code for an agent in another network |
| `PUT /api/v1/agents/{id}/host` | Admin | Sets what an installed agent reads of its own host |
| `DELETE /api/v1/agents/{id}` | Admin | Removes an agent in another network and revokes its key |
| `GET /api/v1/sql-servers` | Admin | The SQL Servers agents read, each with its state in words |
| `POST /api/v1/sql-servers` | Admin | Adds a SQL Server; its agent reads it within 30 seconds |
| `PUT /api/v1/sql-servers/{id}` | Admin | Changes a SQL Server; without `password` the stored one stays |
| `DELETE /api/v1/sql-servers/{id}` | Admin | Removes a SQL Server; the data it sent stays until retention |
| `GET /api/v1/integrations` | Admin | Every monitored SQL Server as an integration, with its template version and modules |
| `GET /api/v1/integrations/modules/sqlserver` | Admin | The SQL Server template's modules, their defaults, and their interval choices |
| `GET /api/v1/integrations/sqlserver/{id}` | Admin | An integration's package page: version, Collector, validation, modules, checks, next steps |
| `POST /api/v1/integrations/sqlserver/{id}/version` | Admin | Moves an integration to the current (Update) or previous (Roll back) template version |
| `POST /api/v1/integrations/probes` | Admin | Starts a connection probe through an agent: Test connection or Validate now |
| `GET /api/v1/integrations/probes/{id}` | Admin | A probe: pending, its result, or why it failed |
| `GET /api/v1/postgres-servers` | Admin | The PostgreSQL servers agents read, each with its state in words |
| `POST /api/v1/postgres-servers` | Admin | Adds a PostgreSQL server; its agent reads it within 30 seconds |
| `PUT /api/v1/postgres-servers/{id}` | Admin | Changes a PostgreSQL server; without `password` the stored one stays |
| `DELETE /api/v1/postgres-servers/{id}` | Admin | Removes a PostgreSQL server; the data it sent stays until retention |

## The version: `GET /api/v1/about` and `/about/releases`

`GET /api/v1/about` answers `{ "version": "0.4.1", "build": "3f9a1c0b2d4e" }`. `build` changes whenever the served
app changes: an open page compares it with the one it loaded and offers to reload (ADR 0030). Without a built app it is
the version.

`GET /api/v1/about/releases` answers the notes of `CHANGELOG.md`, newest first; the first is the running version:

```json
[
  {
    "version": "0.4.1",
    "date": "2026-11-27",
    "summary": "The Console look and its Overview, incidents, job runs, the SQL Server wizard, and release notes.",
    "sections": [{ "title": "Added", "items": ["Tiles open their details: every figure, chart, and row ..."] }]
  }
]
```

`date` and `summary` are `null` when the changelog gives none.

## Searching logs: `GET /api/v1/logs`

| Parameter | Default | Meaning |
| --- | --- | --- |
| `from`, `to` | the last hour | The time window, at most 31 days |
| `severity` | all | The lowest severity, for example `Warning` |
| `service` | all | Only these services; repeat for several |
| `text` | none | Words that must all appear in the message, ignoring case |
| `traceId` | none | Only records of this trace |
| `spanId` | none | Only records of this span, as a trace's chosen span asks (VEY-67) |
| `environment` | all | Only these environments; repeat for several |
| `attribute` | none | `key:value`, for example `http.route:/pay`; repeat to require several |
| `host` | all | Only these hosts, by `host.name`; repeat for several (ADR 0024) |
| `level` | all | Only exactly these levels, for example `Warning`; repeat for several. Beside `severity` (ADR 0024) |
| `q` | none | A query of the log bar, at most 2,000 characters (see [the query language](#the-query-language)) |
| `limit` | 100 | Page size, 1 to `Veyra:Query:MaxPageSize` (1,000) |
| `cursor` | none | `nextCursor` from the previous page |
| `order` | `newest` | `newest` first, or `oldest` first, as a trace lists its records (VEY-68); not with words |

The answer is `{"items": [...], "nextCursor": "..."}`, in the order asked. `nextCursor` is `null` on the last page.
The order is part of the read, so `order=oldest` starts at the oldest record of the window however many newer ones
there are. `400` when `order` is neither, or is `oldest` with words in `text` or `q`.

Each record has `id`, `timestamp`, `observedTimestamp`, `severity`, `severityText`, `body`, `serviceName`,
`environment`, `hostName` (the resource's `host.name`, or `null`), `traceId`, `spanId`, `attributes`, and
`resourceAttributes`.

### The query language

`q` is parsed on the server (ADR 0024) into the filters above, and ANDed with any of them given as their own
parameters. It is the same for `GET /api/v1/logs`, `/logs/summary`, `/logs/export`, and `/logs/stream`.

- **Terms.** Terms are joined by `AND`, or by a space, which means the same. `AND` is upper case; `and` is a word.
  There is no `OR` or `NOT` yet: either is an error, so no query quietly means something else.
- **Words.** A bare word, or a "quoted phrase", is matched against the message, ignoring case; a record needs every
  one. A phrase with a space in it must appear as written; its words also count as words, so the word index finds it.
- **Fields.** `field:value`, or `level>=value`. Field names are any case. A value with spaces is quoted:
  `service:"Orders API"`. Inside quotes, `\"` is a quote and `\\` a backslash. A bare value runs to the next space
  and may hold colons, as `url:http://shop:8080/cart` does.

| Term | Meaning | Same as |
| --- | --- | --- |
| `service:` | the service; repeated, any of them | `service` |
| `env:`, `environment:` | the environment; repeated, any of them | `environment` |
| `host:` | the host; repeated, any of them | `host` |
| `level:` | exactly this level; repeated, any of them | `level` |
| `level>=` | this level or worse | `severity` |
| `trace:` | the trace ID | `traceId` |
| `span:` | the span ID | `spanId` |
| any other `key:value` | a record attribute with exactly this value | `attribute` |

Levels are `trace`, `debug`, `info` or `information`, `warn` or `warning`, `err` or `error`, `fatal` or `critical`,
and `unspecified`, in any case.

**ANDed with the parameters.** A field given both ways keeps only the values both allow: `service=orders` with
`q=service:payments` matches nothing, `level>=` and `severity` keep the worse of the two, and words are added to
`text`. Two different traces, spans, or values of one attribute match nothing.

**The time.** The query never holds the time: the window is `from` and `to`, the shell's range (ADR 0022). A `time:`
term is an error that says so.

**Errors.** A query that does not parse is answered `400` with a problem that says where and what was expected, so
the bar can underline it. `position` is a UTF-16 offset from 0, as JavaScript counts:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Expected a value after service:.",
  "status": 400,
  "position": 8,
  "expected": "a value"
}
```

## The terms of a query: `GET /api/v1/logs/parse`

Takes `q` and answers its terms, in the order written, so the Console's chips show and edit the text without
interpreting it (ADR 0024):

```json
{
  "terms": [
    { "field": "service", "op": ":", "value": "Orders API", "start": 0, "end": 20, "key": null },
    { "field": "level", "op": ">=", "value": "warn", "start": 25, "end": 36, "key": null },
    { "field": "attribute", "op": ":", "value": "/pay", "start": 37, "end": 52, "key": "http.route" },
    { "field": "text", "op": null, "value": "timed out", "start": 53, "end": 64, "key": null }
  ]
}
```

That is `service:"Orders API" AND level>=warn http.route:/pay "timed out"`.

- `field` is `text` for a word or phrase, else `service`, `environment`, `host`, `level`, `trace`, `span`, or
  `attribute`. `env:` answers `environment`.
- `op` is `:` or `>=`; `null` for a word or phrase. `key` is the attribute's key, `null` for other terms.
- `value` is without its quotes and escapes. `start` and `end` are UTF-16 offsets in `q`, `end` exclusive, so
  `q.slice(start, end)` is the term as written, quotes included. `AND` is not a term.
- An empty `q` has no terms. One that does not parse is `400`, as above.

## Counting logs: `GET /api/v1/logs/summary`

Takes the same filters as `GET /api/v1/logs`, without `limit` and `cursor`, and answers with the counts behind the
Log Explorer's histogram, its levels, and its service filter:

```json
{
  "bucketSeconds": 120,
  "buckets": [
    {
      "start": "2026-11-09T06:34:00Z", "total": 118, "errors": 2,
      "levels": { "Information": 110, "Warning": 6, "Error": 2 }
    }
  ],
  "services": [{ "name": "payments-gateway", "count": 4812 }],
  "total": 4812,
  "errors": 61,
  "levels": { "Debug": 120, "Information": 4401, "Warning": 230, "Error": 58, "Fatal": 3 }
}
```

- `buckets` covers the whole window, oldest first, with empty buckets included. The width is the first of 30 s,
  1, 2, 5, 10, 15, and 30 min, 1, 2, 6, and 12 h, or 1 day that gives at most 40 buckets.
- Buckets are aligned to the clock, in UTC: the first starts with the window and ends at the first round time, the
  others start on whole multiples of the width. So there can be one bucket more than the window divided by the width.
  Counting reads counts kept per minute and hour (ADR 0015), so it takes the same time for an hour as for 14 days.
- `errors` counts records at `Error` or worse.
- `total` and `errors` add up the whole window on the server (VEY-68), so the screen counts nothing itself.
- `levels` (ADR 0024), in each bucket and for the whole window, counts the records per level, keyed by severity
  name: `Unspecified`, `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. A level without records is left
  out, so `{}` is an empty bucket.
- The minute and hour counts keep the severity and the environment, so `severity`, `level`, and `environment` are
  counted from them. Words, phrases, attributes, a trace, a span, or a host count the records of the window
  instead; a host through its own index on host and time.
- `services` lists the 50 busiest services, most first. It ignores the `service` filter, so the UI can show what
  else there is to pick.

## Exporting logs: `GET /api/v1/logs/export`

Takes the filters of `GET /api/v1/logs`, without `limit`, `cursor`, and `order`, and `format`: `ndjson` (the default)
or `csv`. It writes every matching record, newest first, as an attachment named like
`veyra-logs-20261126T120000Z.ndjson` (ADR 0024). Any Viewer may export.

- **How it is written.** The records are read page by page with the search's cursor and sent as they are read, so
  the export's memory stays that of one page whatever its size.
- **NDJSON**, `application/x-ndjson`: one record a line, shaped as `GET /api/v1/logs` shapes it.
- **CSV**, `text/csv; charset=utf-8`, RFC 4180 with CRLF line ends: a header row `timestamp`, `observedTimestamp`,
  `severity`, `severityText`, `serviceName`, `environment`, `hostName`, `traceId`, `spanId`, `body`, `attributes`,
  `resourceAttributes`, then one row per record. Times are ISO 8601; attributes are JSON text. A value that starts
  like a spreadsheet formula (`=`, `+`, `-`, `@`, a tab, or a carriage return) gets a leading `'`, so a spreadsheet
  shows it instead of running it.
- **The limit.** An export stops at `Veyra:Query:MaxExportRecords` records (100,000 by default; Settings, Advanced,
  Export). When more records match, the file ends with a line that says so: `{"truncated":true,"limit":100000}` in
  NDJSON, `# truncated at 100000 records` in CSV. A file without that line holds the whole result.
- `400` with a problem when a filter is wrong, `q` does not parse, or `format` is neither; once the file has started,
  a dropped connection simply ends it.

## Live: `GET /api/v1/live` and `GET /api/v1/logs/stream`

Both answer `text/event-stream` (server-sent events, ADR 0022) and run until the browser leaves. They open with the
comment line `: open`, send `: heartbeat` at least every 15 seconds so proxies keep the connection, and are sent
unbuffered with `Cache-Control: no-cache, no-transform` and `X-Accel-Buffering: no`. Ingestion hands each batch it
has stored to them; a slow browser never slows ingestion (see [ingestion](../architecture/ingestion.md#live-streams)).

`GET /api/v1/live` takes `environment` (repeat for several; all by default, records without one included) and sends:

```text
event: rate
data: {"logs":12.4,"spans":3.1,"points":210}

event: tick
data: {"at":"2026-11-26T12:00:05+00:00"}
```

- `rate`, at once and then every second: what was written per second over the last ten whole seconds.
- `tick`, every 5 seconds: screens fetch again on it.

`GET /api/v1/logs/stream` takes the filters of `GET /api/v1/logs`: `severity`, `service`, `text`, `traceId`,
`spanId`, `environment`, `attribute`, `host`, `level`, and `q`. There is no window, page, or order: only records
written from now on are sent, filtered on the server, in the order written.

```text
event: record
data: {"id":5012,"timestamp":"…","observedTimestamp":"…","severity":"Error","severityText":"ERROR",
       "body":"payment declined","serviceName":"payments","environment":"production","hostName":"web-1",
       "traceId":null,"spanId":null,"attributes":{},"resourceAttributes":{"service.name":"payments"}}

event: skipped
data: {"count":37}
```

- `record` is one record as `GET /api/v1/logs` shapes it, on one data line (wrapped above). Its `id` numbers the
  records written on this instance and grows in write order; it is not the storage ID.
- `skipped` says how many matching records were dropped since the last one because the browser fell behind: each
  stream keeps at most 1,000 records waiting and drops the oldest first.
- `text` matches the words of the message as letters and digits, ignoring case, as the word search does for
  ordinary words.

At most `Veyra:Query:LiveListeners` streams (50 by default, both kinds together) are open on one instance; one more
is answered `503` with a problem body until another closes. The setting is on the Settings screen under Advanced and
applies to the next stream that opens. `400` when an `attribute` is not `key:value`, or `q` does not parse.

## Search: `GET /api/v1/search`

| Parameter | Default | Meaning |
| --- | --- | --- |
| `q` | none | The text to find, in any case, at most 200 characters; empty or blank answers every group empty |
| `environment` | all | Only this environment; hosts, containers, and database servers without one are in every one |
| `from`, `to` | the last 24 hours | The window, at most 31 days |

```json
{
  "query": "pay",
  "services": {
    "items": [{ "kind": "service", "name": "Payments.API", "health": "Caution", "detail": "742 ms p95" }],
    "partial": false
  },
  "hosts": {
    "items": [{ "kind": "container", "name": "payments-5c2", "health": "Ok", "detail": "memory 64%" }],
    "partial": false
  },
  "databases": {
    "items": [{
      "kind": "database", "name": "PaymentsDB", "health": "Ok", "detail": "22 GB",
      "engine": "microsoft.sql_server", "server": "SQL-PROD-01"
    }],
    "partial": false
  },
  "traces": {
    "items": [{
      "kind": "operation", "name": "POST /payments", "service": "Payments.API", "count": 18,
      "detail": "18 in 24 h, 2 failed"
    }],
    "partial": false
  },
  "logs": { "count": 4812, "partial": false }
}
```

- **Groups.** Each holds up to 5 items. `kind` is `service`, `host`, `container`, `server` (a database server, named
  by its server), `database`, or `operation` (a root span's name, with its `service` and its traces in the window).
  `engine` and `server` are present for `server` and `database` items only.
- **Where names come from.** Services, hosts, containers, and database servers that sent metrics or logs in the
  window; databases named by their servers' metrics; operations from the root spans of the window.
- **Ranking.** Names that are the text come first, then names that start with it, then names that hold it, each by
  name, ignoring case. `%` and `_` are matched literally.
- **Health** comes from the open alerts (ADR 0007), as on the home: `Ok`, `Caution`, or `Warning`; `null` when the
  group ran out of time before the alerts were read.
- **Detail.** A service's request latency at the 95th percentile over the last five minutes, else its log records in
  the window; a host's fullest disk; a container's memory in use of its limit; a server's databases; a database's
  size; an operation's traces and failures in the window. `null` when there is none.
- **Logs.** `count` is the number of records in the window whose message holds every word of `q`, counted through the
  word index (ADR 0016).
- **Speed.** Every group has 150 ms of its own and runs beside the others. A group that runs out answers with what it
  had found and `partial: true`; the operations and the log count are read newest hours first, so a partial count
  covers the most recent part of the window.
- `400` when the window is not positive or longer than 31 days, or `q` is longer than 200 characters.

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

Also takes `slow=true` (only traces at least as slow as the 95th percentile of `/durations`), `order`, `limit` (1 to
`Veyra:Query:MaxPageSize`, default 50), and `cursor` from the previous page.

| `order` | Order |
| --- | --- |
| `newest` (default) | Newest first, by the root span's start |
| `worst` | Failed traces first, then the longest, then by trace ID (ADR 0029) |

Any other `order` answers `400` with a problem. A `cursor` continues the order it came from; one of the other order
answers `400`.

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
- Pages by keyset in either order: the 20th page costs what the first does. Each order has its own root-span
  indexes, so a page is read in order from an index alone.

### `GET /api/v1/traces/durations`

```json
{ "points": [{ "traceId": "4bf9…", "start": "2026-11-09T12:00:00Z", "durationMs": 1840.2, "failed": true }],
  "read": 20000, "maxRead": 20000, "p95Ms": 420.5, "failed": 412, "slow": 950 }
```

Reads at most the newest 20,000 traces of the window, so it costs the same however busy the window was; `read` says
how many were read. `points` holds every failed trace, every trace at least as slow as `p95Ms`, and an even sample
of the rest, at most 1,500, oldest first. `failed` and `slow` count every trace read that failed, and every one that
did not fail and took at least `p95Ms` (VEY-68), though `points` may hold fewer of them.

### `GET /api/v1/traces/services`

`[{ "service": "checkout-web", "traces": 695, "p95Ms": 412.5, "failed": 12 }]`: the services among the newest
20,000 traces of the window, most traces first, then by name. `p95Ms` is the 95th percentile of the service's
traces read, by nearest rank as `/durations` has it, and `failed` how many of them failed (ADR 0029). `order=slowest`
lists the highest `p95Ms` first instead; `order=traces` is the default, and any other `order` answers `400`. A
`service` filter is ignored, so the UI can show what else there is to pick.

### `GET /api/v1/traces/summary`

The figures of the window under the same filters (ADR 0029), counted on the server:

```json
{ "traces": 48210, "failed": 412, "slow": 2411, "p50Ms": 84.2, "p95Ms": 420.5, "services": 9,
  "previousTraces": 45120 }
```

- `traces`, `failed`, and `services` count every trace of the window, read from the root-span indexes alone.
- `p50Ms` and `p95Ms` are the median and 95th percentile of the newest 20,000 traces, by nearest rank, the same
  `p95Ms` as `/durations`; `null` when the window has no trace.
- `slow` counts the traces of the window at least as slow as `p95Ms`: the traces `slow=true` lists.
- `previousTraces` counts the traces of the equally long period just before `from`, under the same filters, for the
  change; `null` only when that period would start before the earliest time there is.

### `GET /api/v1/traces/{traceId}`

The spans that have arrived, in the waterfall's tree order, and the `summary` of all of them. `at`, when the trace
is known to have happened (from a log record or the list), makes Veyra read that day and the days next to it first;
without it every day kept is read. `errorsOnly=true` lists only the failed spans and every span above them
(VEY-67); the `summary` stays that of every span, so the screen's figures and timeline do not change. `400` when the
ID is not 32 hexadecimal characters, `404` when no span of it is stored.

```json
{ "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spans": [{ "spanId": "00f067aa0ba902b7", "parentSpanId": null, "name": "POST /api/checkout", "kind": "Server",
              "start": "2026-11-09T12:00:00Z", "durationMs": 1840.2, "status": "Error", "statusMessage": "502",
              "service": "checkout-web", "environment": "production", "host": "web-02",
              "attributes": { "http.route": "/api/checkout" }, "resourceAttributes": { "service.name": "checkout-web" },
              "events": [{ "timestamp": "2026-11-09T12:00:01.7Z", "name": "exception",
                           "attributes": { "exception.type": "System.TimeoutException" } }],
              "links": [], "dropped": { "attributes": 0, "events": 0, "links": 0 },
              "depth": 0, "children": 2, "missingParent": false }],
  "summary": { "start": "2026-11-09T12:00:00Z", "durationMs": 1840.2, "spans": 5, "failed": 3,
               "missingParents": 1, "missingParentServices": ["inventory"],
               "services": [{ "service": "checkout-web", "spans": 1 }],
               "criticalPath": ["00f067aa0ba902b7"], "cause": "53995c3f42cd8ad8" } }
```

- `spans` come depth first from each root, children by start (VEY-68). A span whose parent has not arrived is a root
  of its own, after the real root, with `missingParent`. `depth` counts its ancestors among the spans sent and
  `children` its direct children, so the screen only hides the spans under a collapsed one. `attributes` and
  `resourceAttributes` come sorted by key.
- `summary.services` counts the spans per service, most first, then by name. `missingParents` counts the spans whose
  parent has not arrived, of the `missingParentServices`.
- `criticalPath` holds the spans that decided how long the request took: from the end of each span on the path, the
  children that finished before the time reached so far, newest first; a child that overlapped work on the path is
  left out.
- `cause` is the failed span none of whose children failed, a span with an exception first; `null` when nothing
  failed. The screen opens on it.

The log records of a trace come from `GET /api/v1/logs?traceId=…`, and those of one span from
`GET /api/v1/logs?traceId=…&spanId=…`.

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
              "expected": [{ "start": "2026-11-07T08:00:00Z", "load": 1.1 }], "daysOfHistory": 3,
              "records": 240000, "errors": 626, "averageRecords": 10000 },
  "storage": { "usedBytes": 1288490188, "budgetBytes": 21474836480, "dailyBytes": 1181116006,
               "retentionDays": 30, "daysUntilBudget": 17, "reachesBudget": true },
  "estate": {
    "services": [{ "id": "service:production/billing-worker", "name": "billing-worker", "…": "…" }],
    "servicesWorstFirst": [{ "id": "service:production/payments-gateway", "health": "Warning", "…": "…" }],
    "serviceCounts": { "warning": 1, "caution": 1, "ok": 6, "unknown": 0 },
    "hosts": [{ "name": "web-01", "health": "Ok", "filling": true,
                "disk": { "host": "web-01", "mountpoint": "/", "ratio": 0.81, "daysUntilFull": 23 } }],
    "hostCounts": { "warning": 0, "caution": 0, "ok": 2, "unknown": 0 },
    "databases": [],
    "databaseCounts": { "warning": 0, "caution": 0, "ok": 0, "unknown": 0 },
    "diskOutlook": { "filling": [{ "host": "web-01", "mountpoint": "/", "ratio": 0.81, "daysUntilFull": 23 }],
                     "hosts": 2, "allAlerted": false },
    "alertCounts": { "warnings": 1, "cautions": 1 }
  }
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
- `hourly.records` and `hourly.errors` add up the observed hours; `hourly.averageRecords` is the average of the hours
  past, without the one still filling, which scales the expected hours (VEY-68).
- `estate` is what the dashboards draw of the estate, worked out on the server so the browser only lays it out
  (VEY-68). `services` are by name and `servicesWorstFirst` the same services, warnings first, then cautions, for a
  home with room for only some. `hosts` are the hosts with a host block or disks, by name; each draws the disk that
  fills soonest within 30 days (`filling`) or else its fullest. `databases` are the database servers with an open
  alert. The counts are per health. `diskOutlook.filling` holds the disks that fill within 30 days and have no open
  alert, the soonest first; `allAlerted` says some fill that soon but every one has an alert, which Needs you tells.
  `alertCounts` counts the open alerts the home tells by severity.
- `storage.reachesBudget` is `null` without a budget or before the first full day. `false` means retention keeps
  storage under the budget; then `daysUntilBudget` is `null`.

### `GET /api/v1/home/environments`

The environment switcher's list (VEY-67). `search` keeps the environments whose names hold it, in any case. `all` is
the choice of every environment together, with the worst `condition` of any, so a storm elsewhere is never hidden;
it is `null` when the search does not match "All environments".

```json
{ "all": { "condition": "Storm" },
  "environments": [{ "name": "production", "condition": "Storm", "errorRatio": 0.058 }] }
```

## Metrics: `/api/v1/metrics`

The Metrics screens (ADR 0010). Every endpoint, the database server's tabs that take a window included, takes the
window as `from` and `to` (ISO 8601, UTC, such as `2026-11-26T09:00:00Z`), or as `range`, a window ending now:
`15m`, `1h`, `6h` (the default), `24h`, `7d`, or `30d` (ADR 0022). With only `to`, the window is `range` before it;
with only `from`, it ends now. A window that is not positive or longer than 31 days is `400`, as for logs. Metrics are
kept 30 days (`Veyra:Storage:MetricRetentionDays`); a window reaching further back answers what is kept.

The server chooses the step, `bucketSeconds`: the shortest of 1, 5, 15, or 30 minutes for a window of up to six
hours, read from the points and the 5-minute rollups, and of 1, 3, 6, 12, or 24 hours beyond that, read from the
hourly rollups (ADR 0015), that gives a screen's lines at most 180 points: one minute for `1h`, 5 minutes for `6h`,
one hour for `24h` and `7d`, and 6 hours for `30d`. Buckets start on multiples of their width, the last one holding
the window's end. A point's `value` is `null` where nothing arrived. "Now" numbers, such as `cpuRatio`, cover the
last five minutes before the window's end. Answers that state their `range` (`custom` for a from–to window) also
state the window as `from` and `to`; the chart does too.

### `GET /api/v1/metrics/overview`

| Parameter | Default | Meaning |
| --- | --- | --- |
| `range` | `6h` | The window of the lines, ending now: `15m`, `1h`, `6h`, `24h`, `7d`, or `30d` |
| `from`, `to` | none | The window instead, in ISO 8601 UTC, up to 31 days apart |
| `search` | none | Text a card's name must hold, in any case: a host, service, container, or database server (VEY-67) |
| `environment` | all | One environment; hosts, database servers, and containers that name none belong to every one |

The server narrows its series reads to the search and the environment before their limits, so a host is found among
any number of series, and then holds each card to them exactly.

```json
{
  "at": "2026-11-10T11:00:20Z", "range": "6h", "bucketSeconds": 300,
  "from": "2026-11-10T05:00:20Z", "to": "2026-11-10T11:00:20Z",
  "usage": { "series": 1284, "maxSeries": 50000, "pointsPerSecond": 38, "usedBytes": 432013312,
             "retentionDays": 30, "names": 412 },
  "summary": { "hosts": 3, "fillingDisks": 1, "services": 3, "databases": 1, "containers": 3, "restarting": 1,
               "anySent": true },
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

- `summary` counts the cards listed: hosts, those with a `fillingDisk`, services, database servers, containers, and
  those that restarted within the range. `anySent` is false when nothing sent metrics at all, so the screen can tell
  how to start from "nothing matches".
- `hosts` are the hosts that sent `hostmetrics` in the last 15 minutes, by `host.name`. `fillingDisk` is the disk
  that fills up soonest within 30 days, by a straight line through the last day of usage, or `null`. Every disk has
  `daysUntilFull`, `null` when it is not filling up or would take more than a year.
- `services` are the services that sent HTTP server or runtime metrics in the last 15 minutes, per environment.
  `serverErrorRatio` is the share of requests answered with a 5xx status.
- `databases` are the database servers that sent in the last 15 minutes (ADR 0018), by `db.system.name` and
  `server.address`, listed by the server's name whatever its engine (VEY-68). `deadlocks` counts the last 15
  minutes; `oldestBackupSeconds` is the longest any database has gone without a full backup; `failedJobs` counts
  SQL Agent jobs whose last run failed. A PostgreSQL server (`engine` `postgresql`, VEY-33) has no `cpus`,
  `memoryBytes`, `uptimeSeconds`, `batchRequests`, or `pageLifeSeconds`; its `connections` are those to every database, and it adds `maxConnections` (its
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
- The `cpu` and `latency` lines have at most 72 points, by the same step rule: `bucketSeconds` is 60 for `1h`, 300
  for `6h`, 3600 for `24h`, 10800 for `7d`, and 43200 for `30d`. The host and service screens draw their charts with
  the finer buckets of the window.

### `GET /api/v1/metrics/hosts/{host}` and `/services/{service}`

One host or service in detail, with its card as on the overview. A service takes `environment`; leave it out for a
service that names none. Both answer `404` when nothing arrived in the range.

- **Host.** `cpu` (total, user, system, iowait as shares), `memory` (used, cache as shares), `disks`, `network`
  and `diskIo` (bytes per second, by direction), `load` (1m, 5m, 15m), and `containers`: the Docker containers on
  the host that sent within the range, by name, each as on the overview, with `restarts` counted over the range.
  `fillingDisks` counts the disks that fill at their pace and `restartingContainers` the containers that restarted
  within the range, counted on the server (VEY-68).
- **Service.** `latency` (p50, p95, p99 in seconds), `requests` (ok, 4xx, 5xx per second), `routes` (the busiest
  ten in the last five minutes, with `requestsPerSecond`, `p95Seconds`, and `serverErrorRatio`), `resources` (cpu
  share, memory bytes), `runtime` (garbage collections per minute, thread pool queue, threads), and
  `instanceNames`. `slowestRoutes` holds up to five routes, the slowest first by `p95Seconds`, picked from every
  route and not only the busiest ten, for the Simple view (VEY-68).

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
- **`overview`**: the figures the Overview tab tells beside its weather, from the lists as sent (VEY-68): `topWait`
  (the wait category that took the most time and its `share` of all, `null` when nothing waited), `lateBackups` (the
  databases, tempdb aside, without a full backup within 26 hours), `failedJobs`, and `oldestWraparound` (the
  PostgreSQL database closest to transaction ID wraparound, `null` for SQL Server).
- **PostgreSQL** (`/api/v1/metrics/databases/postgresql/pg-main`, VEY-33) reads the tested PostgreSQL configuration
  instead: `requests` is transactions per second, `connections` the connections to every database, and `blocked`
  the sessions waiting for a lock longer than 30 seconds; `pageLife`, `waits`, `jobs`, `running`, and `topQueries`
  are empty. It adds the lines `maxConnections`, `commits`, and `rollbacks` (per second), which are `null` for SQL
  Server. Each of `databases` adds `connections`, `transactionIdAge`, `wraparoundRatio` (that age of 2^31, from 0
  to 1), `liveRows` and `deadRows` of all its tables, `deadRowsRatio` (dead of all rows), and `vacuums` (of its
  tables over the last day); `logBytes`, `backupAgeSeconds`, and `ioLatencySeconds` are `null`. **`tables`** lists
  the ten tables with the most dead rows, the most first, each with `database`, `schema`, `name`, `liveRows`,
  `deadRows`, `deadRowsRatio`, and `vacuums` over the last day; it is empty for SQL Server.
- **`system=true`** (VEY-64, VEY-66) lists what the server does for itself; without it the server leaves those rows
  out before it cuts any list to its limit, so the rows sent are the applications': on `databases`, the engine's own
  (SQL Server's master, model, msdb, tempdb; PostgreSQL's postgres and templates); on `jobs`, those SQL Server sets
  up, such as `syspolicy_purge_history`; on `topQueries`, statements in a system database or that read only the
  catalog, DMVs, system tables, or server properties, such as SSMS's Object Explorer and change tracking's cleanup;
  on `tables`, catalog tables. What was left out is counted in `hiddenDatabases`, `hiddenJobs`, `hiddenTopQueries`,
  and `hiddenTables`, each with `count` and up to 20 `names` an alert can be about, such as `msdb`. With
  `system=true` the rows carry `system`, and the server's own statements are ranked apart, so they never push the
  applications' out of a list. The `queries`, `jobs`, `storage`, `indexes`, and `server` reads below take the same
  parameter. Alerts watch every row either way. The screen never filters what it receives.

### `GET /api/v1/metrics/databases/{engine}/{server}/overview`

A SQL Server's Overview tab in one read (ADR 0025), as the Console Overview is (ADR 0023). It takes `range` or `from`
and `to` like the other database reads, and `system`; `400` for a window that is not positive or longer than 31 days,
`404` when the server sent neither metrics nor live reads. Lines have one value per bucket of the window, in the steps
of the database screen (at most 180 buckets: minutes up to six hours, whole hours beyond), `null` where nothing was
measured.

- **`from`**, **`to`**: the window.
- **`machine`**: `version`, `updateLevel` (`null` for RTM), `edition`, `osName` and `osRelease` (from
  `sys.dm_os_host_info`, `null` on Azure SQL Database), `cpus`, `memoryBytes` (the machine's physical memory),
  `startedAt` (worked out from the uptime), `uptimeSeconds`, `databases` (those in `inventory`), and `platform`
  (`sqlserver`, `managed-instance`, `azure-sql-database`, or `null` before the agent reported it).
- **`figures`**: `cpu` (SQL Server's share, 0 to 1), `sqlMemory` (bytes), `sessions` (user connections), `blocked`,
  `size` (every database's data and log files, bytes), `lastFullBackupSeconds` (the oldest last full backup of the user
  databases the backup rule judges), `batchRequests` (per second), and `volumeFree` (the fullest volume's free share,
  with its `volume`). Each has `value` (now, at the end of the window), `line`, and `change`: the window's average
  against the equally long period before, as a share of what it was (`0.12` is 12 % more) or, for `cpu` and
  `volumeFree`, as a difference of shares; `null` without a period before. `lastFullBackupSeconds` has no line or
  change.
- **`performance`**: `bucketSeconds`, `starts`, and per bucket `cpu`, `sqlMemoryRatio` (SQL memory of the machine's),
  and `waitMsPerSecond`.
- **`counters`**: `batchRequests` and `compilations` (`per second`), `pageLife` (`seconds`), and `waits`
  (`ms per second`), each the average over the window with its `change`.
- **`io`**: `readBytesPerSecond` and `writeBytesPerSecond` over the window, and `readLine` and `writeLine`.
- **`waits`**: `totalSeconds` and the five `types` that waited longest, then `Other`, each with `category`, `seconds`,
  and `share`. Idle waits, such as `SLEEP_*` and `WAITFOR`, are left out.
- **`storage`**: `totalBytes`; `growth24hBytes`, `growth7dBytes`, and `growth30dBytes`; `line`, the total per day over
  the last 30 days; the eight `largest` databases with `bytes`, `growth24hBytes`, and `growth7dBytes`; and the
  `volumes` with `name`, `sizeBytes`, `freeBytes`, `freeRatio`, and `daysUntilFull` at the pace of the last week, as
  the Storage tab.
- **`inventory`**, the largest first: each database's `dataBytes`, `logBytes`, `growthPerDayBytes` over the last day,
  `state`, and `recoveryModel`. Without `system=true` the server's own databases are left out of `inventory` and
  `largest`.
- **`blocking`**: the `chains` of the newest live read, when it is at most two minutes older than `to`, each with the
  `headSessionId`, its `database`, `login`, `host`, `waitType`, `waitSeconds`, and `statement`, the `blockedCount`
  behind it, and every `blocked` session with its `waitType` and `waitSeconds`, the nearest first; `blockedLine`; and
  the `deadlocks` of the window.
- **`coverage`**: each area of ADR 0020 (`activity`, `waits`, `queries`, `storage`, `backups`, `jobs`, `events`,
  `configuration`, `availability`), whether it is `delivering` (data in the last 15 minutes; an hour for events, which
  arrive only when they happen; 26 hours for the configuration, which Veyra keeps once a day per value), its
  `lastSeen`, and, when it does not deliver, the `missingPermission` that `/permissions` reports for it. An area with
  nothing to report, such as jobs on a server without enabled jobs, shows as not delivering.

### `GET /api/v1/metrics/databases/{engine}/{server}/live`

The server's Live tab (ADR 0020): its newest read of busy sessions, or with `at` (an ISO 8601 time) the read at or
before that moment, within two minutes of it. `404` when the server sent neither reads nor metrics.

- **`sampledAt`**, **`ageSeconds`**: when the read was made and how old it is; `null` without a read.
- **Filters** (VEY-66): `database`, `program`, `host`, `login`, and `wait` list only the sessions with every value
  given, and the chains with at least one of them. The tiles and findings stay the whole server's.
- **`tiles`**: batch requests per second with its line over 15 minutes, `blocked` with its line,
  `longestRequestSeconds` and its `longestRequestSession`, `oldestTransactionSeconds` and its session,
  `grantsWaiting`, `tempdbFreeRatio`, `tempdbSizeBytes`.
- **`findings`**, the worst first: `kind`, `severity` (`ok`, `caution`, `warning`), `title` and `detail` in words for
  anyone, `advanced` in SQL Server's terms, the `session` it is about, and `actions` (`copy` a statement such as
  `KILL 88;`, a program's `logs`, a `session`'s panel).
- **`blocking`**: the chains, each `session` with how many are `waiting` behind it and its `children`.
- **`running`**: the sessions of `sessions` with a request running now, the longest first (VEY-68).
- **`sessions`**: every busy session that meets the filters with its status, wait, blocker, transaction, tempdb, grant,
  hashes, `name` (what the statement does, in words), and `text`.
- **`chained`**: the other sessions of the chains listed, so a chain still names who blocks whom; empty without
  filters.
- **`waits`**: every wait type the sessions wait on, with its `category`, `simple` words, `meaning`, and `causes`.

### `GET /api/v1/metrics/databases/{engine}/{server}/queries`, `/queries/{queryHash}`, `/plans/{planHash}`

The Queries tab and a statement's page (ADR 0020). `queries?range=` lists up to 100 statements, ranked by `sort`:
`totalTime` (the default), `cpu`, `reads`, `runs`, or `timePerRun`; any other value is `400`. The server ranks
before it keeps the 100, so the statement that reads the most is listed even when 100 others took more time (VEY-66).
With `system=true` up to 100 of the server's own are listed apart (VEY-64); without it they are counted in
`hiddenStatements`. With `simple=true` only the statements with a finding are listed, up to 100 by `sort`: each
statement's newest plan is read before the cut, so one outside the busiest is still found (VEY-67). `busiest` is the
statement that took the most time, which the Simple view names when none shows a problem. Each has `name`, `text`,
`database`, `totalSeconds`, `executions`, `cpuSeconds`, `reads`, `perRunSeconds`, `plans`, and `findings` (`kind`,
`severity`, `title`, `detail`, `fix`, `script`); Veyra's agent is counted apart.
`queries/{queryHash}?range=` adds `points` per bucket with the plan that ran most, `plans` with their per-run
figures, `operators`, and `warnings`, and `callers` from the live view; `404` when it did not run in the range.
The tab also lists `queryStore`: each user database, whether it keeps Query Store, and the statement that turns it on,
and `queryStoreOff`, those without it (VEY-68); the page adds `callerSamples`, how many reads saw any program run it,
and `history`, its Query Store intervals, and `waits` by category.
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
`findings`. Without `system=true` the server's own databases are left out with their findings, counted in
`hiddenDatabases` and `hiddenFindings`. `needsDoing` holds the server-wide findings and the listed databases' in one
list, the warnings first, each with its `database` (`null` for the server's own), for the Needs doing panel
(VEY-68). `404` before the server's first storage read.

### `GET /api/v1/metrics/databases/{engine}/{server}/indexes`

Index and statistics advice (ADR 0020): `findings` with the statement to run, and the 100 largest `indexes` with
their keys, size, reads, writes, and lock escalations since SQL Server started. Without `system=true` the system
databases are left out of both before the cut to 100, counted in `hiddenIndexes`. With `simple=true` only the five
worst `findings` are sent, and no `indexes`, as the Simple view shows (VEY-67).

### `GET /api/v1/metrics/databases/{engine}/{server}/server`

The server as a whole over `range` (ADR 0020): version, update level, edition, uptime, whether it is out of support,
CPU of SQL Server and of others now and as lines, runnable tasks, workers, memory, grants waiting, dumps of the last
day, SQL Server Agent, batch requests now and usual for this hour of the week, the 15 wait types with the most time
explained, and `findings`. Without `system=true` idle waits are left out of `waits`, counted in `hiddenWaits`; the
findings read every wait. `404` when it sent nothing in the range.

### `GET /api/v1/metrics/databases/{engine}/{server}/jobs`

The server's SQL Agent jobs (ADR 0020): `jobs`, those that need a look first, each with `failed`, `lastRun`,
`lastSeconds`, `runningSeconds`, `nextRun`, `late`, `usualSeconds`, and the `runs` of the last week with their
`steps` and messages, and `needsLook`: the last run failed, it is late, or it has run at least five minutes and twice
its usual time; and `hiddenJobs`, those SQL Server set up itself, left out without `system=true` (VEY-66). With
`simple=true` only the jobs that need a look are listed; `enabled` counts the jobs listed before that, so the Simple
view can tell a server without jobs from one whose jobs are all well (VEY-67).

### `GET /api/v1/metrics/databases/{engine}/{server}/events`

The server's `system_health` events over `range` (ADR 0020), the newest first: `at`, `name`, `severity`, `words` for
anyone, `error`, `errorSeverity`, `wait`, and `message`. With `simple=true` the events whose `severity` is `ok` are
left out, as the Simple view shows (VEY-67). Storage leaves them out before it reads the newest 1,000 records of the
range (VEY-68), so an event that needs something is found behind any number of quiet ones.

### `GET /api/v1/metrics/databases/{engine}/{server}/availability`

The server's Availability Group `replicas` (role, healthy), `databases` (queue, lag, synchronizing), `logShipping`
(side, behind, delay), and cluster `nodes` (state, owner) (ADR 0020); empty lists for a server without them.
`trouble` says in words each replica that is not healthy, each database that is not synchronizing, and each log
shipping that is behind (VEY-68).

### `GET /api/v1/metrics/databases/{engine}/{server}/backups`

The server's backup history (ADR 0025), from the backups the agent reads from `msdb` every 5 minutes. It takes `range`
or `from` and `to`, and `system`; `400` for a window that is not positive or longer than 31 days, `404` when the
server sent neither backups nor backup ages. Days are UTC.

- **`from`**, **`to`**: the window. **`selfManaged`**: `true` for Azure SQL Database and Managed Instance, which back
  up by themselves and keep no `msdb` history the agent reads.
- **`backups`**: those that finished in the window, the newest first, at most 500: `database`, `type` (`full`,
  `differential`, `log`), `startedAt`, `finishedAt`, `seconds`, `bytes` and `compressedBytes` (`null` when `msdb`
  recorded none), `copyOnly`, and `device` (the file or URL it went to).
- **`days`**: every day the window touches, the oldest first, each whole day counted: `day` (`yyyy-MM-dd`) and for
  `full`, `differential`, and `log` the `count` and the `seconds` they took together.
- **`grid`**: the 7 `days` up to the day of `to`, and each of the `databases` with one `cells` entry per day: `full`
  and `differential` (whether one finished) and `log` (how many).
- **`freshness`**, the worst first: each database's `fullAgeSeconds`, `diffAgeSeconds`, `logAgeSeconds` (`null` in
  simple recovery), `recoveryModel`, `state`, and `policy`, a sentence such as "Full backup within 26 h, warning after
  50 h; log backup within 1 h, warning after 4 h". The `state` uses the backup rules' thresholds as saved on Settings,
  so it agrees with the alerts: `warning` or `caution` when a threshold is passed, or when a database never had a full
  backup; `ok`; or `ignored` when no rule judges it, such as a database the backup-age rule ignores on purpose.

tempdb is never listed. Without `system=true` the server's own databases are left out of every part.

### `GET /api/v1/metrics/databases/{engine}/{server}/configuration`

The server's configuration and security posture (ADR 0020, area 13): `settings` (`name`, `value`, `recommended`),
`findings` with their fix, the `sysadmins`, and the `changes` of settings and sysadmin members in the last 30 days, the
newest first (`at`, `item`, `from`, `to`).

### `GET /api/v1/metrics/databases/{engine}/{server}/permissions`

What the monitoring login may not read (VEY-58): `reported` (whether the agent reported its permissions in the last 15
minutes), `login`, `platform`, the `gaps` (each `permission`, the `tabs` it leaves empty, and what Veyra `reads` with
it), and the `script` that grants every missing permission, `null` for none.

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
and at most eight `lines`, the highest first; the others are left out, not folded into one line.

```json
{ "lines": [{ "label": "web-01", "latest": 1.6, "average": 1.21, "highest": 2.4,
              "points": [{ "at": "2026-11-26T11:00:00Z", "value": 0.9 }] }] }
```

Each line's `latest` is its last value, `average` the average of its points that have a value, and `highest` the
highest of them (ADR 0029), in the line's unit and worked out on the server from the points it carries; `null` when
no point has a value. Every chart line the API answers carries them, also on the host, service, and incident
screens.

## Resources: `/api/v1/resources`

The Console look's Resources lists (ADR 0022): every service, host and container, and database server whose metrics
are kept, a page at a time, worst first. Each list is an inventory: a block that sent metrics stays listed for as long
as its series are kept (30 days), and is **silent** when nothing arrived from it for `Veyra:Query:SilentAfterHosts`
(5 minutes, also for containers and database servers) or `Veyra:Query:SilentAfterServices` (2 hours). So a machine
that stopped is listed and counted, never missing. Silence is judged against now, whatever the window.

Every list takes:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `environment` | all | One environment. Services must name it; hosts, containers, and database servers that name none belong to every one |
| `range`, `from`, `to` | `6h` | The window of the figures, as for the metrics endpoints; `400` beyond 31 days |
| `health` | all | `ok`, `caution`, `warning`, `unknown`, or `silent`; repeat it or separate with commas for several |
| `search` | none | Text the name must hold, in any case (for a database server, its server) |
| `sort` | `worst` | `worst` or `name`; any other value is `400` |
| `cursor` | none | The `next` of the previous page, with the same filters; one Veyra did not issue is `400` |
| `limit` | 50 | Items per page, 1 to `Veyra:Query:MaxPageSize` |

- **Health** is `Ok`, `Caution`, `Warning`, or `Unknown`, from the open alerts only (ADR 0007); `silent` is apart.
- **Worst first** is warning, caution, silent, unknown, healthy, then by name. A silent item with an open alert
  ranks by its alert.
- **The header** counts every item the filters keep, not only the page. `total` is the same count.
- **Changes** compare the window with the period of the same length just before it, in the same read: a rate or an
  amount as a share (`0.12` is 12 % more), a ratio in its own unit (`0.0012` is 0.12 percentage points). A change is
  `null` when the period before has no data.
- **The figures** cover the window, in the overview cards' buckets (at most 72, hourly rollups beyond six hours).

### `GET /api/v1/resources/services`

Takes `host` too: only services whose resources name that `host.name`.

```json
{ "header": { "total": 12, "healthy": 10, "warning": 1, "caution": 1, "silent": 1,
              "requests": 1240000, "requestsChange": 0.12, "errorRatio": 0.0038, "errorRatioChange": 0.0012,
              "p95Seconds": 0.214, "p95Change": 0.0, "instances": 28, "hosts": 9 },
  "items": [ { "name": "CustomerSync", "environment": "production", "health": "Warning", "silent": false,
               "requestsPerMinute": 680, "p95Seconds": 0.108, "errorRatio": 0.084, "instances": 3,
               "hosts": ["docker-02"], "lastSeenAt": "2026-11-26T11:59:02Z", "requests": [652.5, 680.1] } ],
  "total": 12, "next": null }
```

- `requests`, `errorRatio`, and `p95Seconds` come from the OpenTelemetry SDK's `http.server.request.duration`;
  `errorRatio` is the share answered with a 5xx status. A service that does not send it, such as a worker without
  HTTP, has `null` for all three and its changes, and a `requests` line of `null`s. The header's `p95Seconds` adds up
  the histograms whose bounds agree with the first.
- `requests` per item is 24 numbers, oldest first: requests per minute in each 24th of the window. Where the buckets
  are wider than a 24th, a slice repeats its bucket's rate.
- `instances` counts `service.instance.id`, or the host where a series has none; `hosts` lists the `host.name` of the
  service's resources.
- `lastSeenAt` is when its metrics last arrived, to within a minute.

### `GET /api/v1/resources/hosts`

Takes `kind` (`host` or `container`) and `state` (`online` or `silent`) too.

```json
{ "header": { "known": 12, "online": 11, "containers": 6, "cpuAverage": 0.31,
              "cpuBusiest": { "name": "payments-5c2", "ratio": 0.71 }, "memoryAverage": 0.58,
              "memoryFullest": { "name": "payments-5c2", "ratio": 0.82 },
              "disksFilling": 1, "disksFillingFirst": { "host": "sql-01", "mount": "D:", "hoursLeft": 38 } },
  "items": [ { "name": "sql-01", "kind": "host", "os": "Windows", "parent": null, "health": "Warning",
               "silent": false, "cpuRatio": 0.24, "memoryRatio": 0.51, "fullestDiskRatio": 0.89,
               "runs": ["SQL-PROD-01"], "runsCount": 1, "lastSeenAt": "2026-11-26T11:59:40Z" } ],
  "total": 18, "next": null }
```

- `known` and `online` count hosts; `containers` counts containers. A host is one item whatever environments its
  series name; a container is one item per host it runs on, with that host as `parent`.
- `cpuRatio` is the window's average; `memoryRatio` and `fullestDiskRatio` their newest value in it. A container's
  `cpuRatio` is of one CPU, so above 1 when it uses more, and its memory is of its limit; it has no disks.
- `runs` names up to three services, database servers, and containers whose series name the host, alphabetically;
  `runsCount` counts them all. A container runs nothing.
- `disksFilling` counts the hosts with a disk full within 30 days at the pace of the last day; `disksFillingFirst`
  is the soonest, `null` when none is filling.

### `GET /api/v1/resources/databases`

Takes `engine` (such as `microsoft.sql_server` or `postgresql`) and `system` too: with `system=true` the servers'
own databases, such as master or postgres, are counted.

```json
{ "header": { "servers": 6, "silent": 1, "databases": 35, "dataBytes": 1.2e12, "growthBytes": 4.1e9,
              "lateBackups": 1, "lateBackupFirst": { "server": "SQL-PROD-01", "database": "Reporting" },
              "blockingChains": 3, "blockingServer": "SQL-PROD-01" },
  "items": [ { "engine": "microsoft.sql_server", "server": "SQL-PROD-01", "version": "SQL Server 2022 CU14",
               "health": "Caution", "silent": false, "databases": 13, "sizeBytes": 6.4e11,
               "growthBytesPerDay": 2.1e9, "lastFullBackupAgeSeconds": 21600, "lateBackups": 1,
               "blockingChains": 3, "cpuRatio": 0.28 } ],
  "total": 6, "next": null }
```

- `sizeBytes` adds up the databases' files at the end of the window; `growthBytesPerDay` their growth at the pace of
  the last day. The header's `dataBytes` and `growthBytes` (per day) add up the servers'; `null` when none reports.
- `lastFullBackupAgeSeconds` is the longest any database, tempdb aside, has gone without a full backup;
  `lateBackups` counts those without one within 26 hours, as the backup-age rule's default caution. PostgreSQL's
  receiver sends no backups, so both are `null` for it. `lateBackupFirst` is the late database that has gone longest.
- `blockingChains` counts the head blockers in the server's newest live read (ADR 0020) of the window's last minute;
  `null` without such a read, as for PostgreSQL or a server the agent does not read live.
- `version` and `cpuRatio` (SQL Server's own CPU, the window's average) come from the agent's
  `sqlserver.server.info` and `sqlserver.cpu.sql.ratio`; `null` for PostgreSQL.

## The Overview: `GET /api/v1/overview`

The Console look's Overview (ADR 0023) in one read, so every figure comes from the same moment and a refresh is one
request. Any signed-in role may read it. It takes `environment` (all when omitted) and the window as the resource
lists do: `range`, or `from` and `to`, 6 hours by default, `400` beyond 31 days or when `from` is not before `to`.
`databases` and `pipelines` say how many rows of each table to answer, from 1 to 50, 12 and 8 when omitted, `400`
outside that: the screen asks for as many as fit in their tiles, so a tall screen shows more (VEY-81).

Nothing is worked out twice: the figures for services, hosts, and databases are the headers of the three resource
lists, read whole with no filter, and the request activity and host trends are the buckets those lists read. The
applications and infrastructure tables use the resource lists themselves; needs attention uses the home snapshot.

```json
{ "from": "2026-11-26T11:00:00Z", "to": "2026-11-26T12:00:00Z",
  "figures": {
    "services": { "healthy": 10, "total": 12, "warning": 1, "caution": 1, "silent": 1 },
    "hosts": { "online": 11, "known": 12 },
    "databases": { "servers": 6, "databases": 35 },
    "requests": { "count": 1240000, "change": 0.12 },
    "errorRatio": { "value": 0.0038, "change": 0.0012 },
    "openAlerts": { "warning": 1, "caution": 2 },
    "jobs": { "succeededRatio": 0.972, "runs": 180, "failed": 5 },
    "availability": { "ratio": 0.9995, "change": 0.0001, "days": 30 } },
  "activity": { "stepSeconds": 60, "points": [ { "at": "2026-11-26T11:01:00Z", "requests": 1234, "errors": 12 } ] },
  "ingestion": { "stepSeconds": 60,
                 "points": [ { "at": "2026-11-26T11:01:00Z", "logs": 1200, "spans": 300, "points": 9000 } ] },
  "hostTrends": { "stepSeconds": 60,
                  "points": [ { "at": "2026-11-26T11:01:00Z", "cpu": 0.31, "memory": 0.58, "disk": 0.71 } ] },
  "pipelines": [ { "name": "Nightly backup", "server": "SQL-PROD-01", "engine": "microsoft.sql_server",
                   "lastRunAt": "2026-11-26T02:00:00Z", "durationSeconds": 12.0, "status": "Failed", "runs": 7,
                   "failedRuns": 1,
                   "jobId": "WyJzcWxhZ2VudCIsbnVsbCwibWljcm9zb2Z0LnNxbF9zZXJ2ZXIgU1FMLVBST0QtMDEiLCJOaWdodGx5IGJhY2t1cCJd" } ],
  "collectors": [ { "name": "Built in", "state": "Online", "lastSeenAt": "2026-11-26T11:59:50Z",
                    "version": "0.140.1", "queue": null } ],
  "databases": { "items": [ { "name": "Reporting", "server": "SQL-PROD-01", "engine": "microsoft.sql_server",
                              "state": "Online", "sizeBytes": 5.0e9, "growthBytesPerDay": 1.2e8,
                              "lastFullBackupAgeSeconds": 111600, "late": true, "connections": null } ],
                 "total": 35 } }
```

- **Steps.** `activity`, `ingestion`, and `hostTrends` have one point per step, oldest first, in the overview cards'
  buckets: the shortest of 1, 5, 15, or 30 minutes up to six hours, or of 1, 3, 6, 12, or 24 hours beyond, that keeps
  a line at most 72 points. `at` is the step's start.
- **Services, hosts, databases.** As the resource lists' headers count them. `databases.databases` leaves the
  servers' own databases out.
- **Requests and error ratio.** From `http.server.request.duration`, as on the Services list. `count`, `value`, and
  both changes are `null` when no service sends it; a change is also `null` when the period before had none.
  `requests.change` is a share (`0.12` is 12 % more), `errorRatio.change` is in percentage points as a ratio.
- **Open alerts.** The alerts open now, by severity, in the environment when one is chosen.
- **Jobs.** Every finished job run that started in the window, from `storage.job_run` (ADR 0027): SQL Server Agent
  runs (ADR 0020, area 10) and runs recognised in traces alike. `runs` counts the finished runs, `failed` those that
  did not succeed (failed, retried, or cancelled); `succeededRatio` is `null` without runs. With an environment
  chosen, jobs whose sender names no environment count too, as on the resource lists. The jobs SQL Server set up for
  itself are left out, as the Jobs tab leaves them out.
- **Availability.** The share of the last 30 days, ending at `to`, in which no service had an open warning, read from
  the alert history: overlapping warnings count their time once; cautions and alerts on hosts, containers, and
  database servers do not count. It is not an uptime probe. `change` compares it with the 30 days before, in
  percentage points as a ratio. `ratio` and `change` are `null` when no service is known.
- **Activity.** `requests` and `errors` (5xx answers) per step across every service with HTTP metrics; both `null`
  in a step no service measured.
- **Ingestion.** The log records, spans, and metric points stored per step, from `storage.ingest_count_minute`
  (see [Ingestion](../architecture/ingestion.md)), by their own time: a record's timestamp, a span's start, a point's
  time. A step with nothing is `0`. With an environment chosen, only telemetry that names it counts.
- **Host trends.** The average CPU and memory in use of the hosts that measured the step, and the fullest real disk of
  any of them, each from 0 to 1; `null` in a step no host measured. Containers are not in it.
- **Pipelines.** The latest run of each job, failed ones first, then the latest first, at most `pipelines` (8 by
  default). `status` is `Succeeded`, `Failed`, or `Running`; a job the agent says runs now at the window's end is
  `Running`, with `lastRunAt` when it started and `durationSeconds` how long it has run. `runs` and `failedRuns`
  count its runs in the window. A SQL Server Agent job has its server in `server` and its engine in `engine`; a traced
  job has its service in `server` and `engine` `null`. `jobId` opens the job: `GET /api/v1/jobs/{jobId}`, and
  `/jobs/{jobId}` on the screen.
- **Collectors.** Veyra's agents, the built-in one first, with the state the Agents page shows (`NotConnected`,
  `Offline`, `ConfigurationFailed`, `Unhealthy`, `Online`), when each last polled, and its Collector version. `queue`
  is always `null` until agents report their exporter queue, rather than an invented number. The whole field is `null`
  for anyone but an Admin.
- **Databases.** Every database of every server on one page of at most `databases` (12 by default), worst first: a
  late backup or a state other than online first, then the largest. `total` counts them all. The servers' own
  databases are left out. `state` is SQL Server's state from the agent's storage read, such as `Online` or
  `Recovery pending`, `null` where it is not sent (PostgreSQL). `late` is a SQL Server database without a full
  backup within 26 hours, tempdb aside; `connections` is the database's connections now where the engine reports them
  per database (PostgreSQL), else `null`.

## Jobs: `/api/v1/jobs`

Jobs and pipelines (ADR 0027): every job Veyra knows from SQL Server Agent's job history and from traces, whose root
spans carry a job name attribute (see [Jobs and pipelines](../integrations/jobs.md)). Their runs are kept in
`storage.job_run` for 30 days, longer than the traces they come from. Any signed-in role may read them. Both take
the window as the resource lists do: `range`, or `from` and `to`, 6 hours by default, `400` beyond 31 days or when
`from` is not before `to`. A run belongs to the window when it started in it.

### `GET /api/v1/jobs`

Takes `environment` (all when omitted; jobs whose sender names none are kept with every one, as on the resource
lists), `source` (`trace` or `sqlagent`), `q` (a job's name or its block's name contains it, in any case), `status`
(`failing`, `running`, or `all`, the default), and `system=true` to list the jobs SQL Server set up for itself, left
out otherwise as on a server's Jobs tab. An unknown `source` or `status` is a `400`.

```json
{ "from": "2026-11-25T12:00:00Z", "to": "2026-11-26T12:00:00Z",
  "figures": { "jobs": 3, "runs": 5, "failed": 2, "successRatio": 0.6, "running": 1 },
  "items": [ { "id": "WyJ0cmFjZSIsInByb2R1Y3Rpb24iLCJiaWxsaW5nIiwibmlnaHRseS1pbXBvcnQiXQ", "name": "nightly-import",
               "source": "trace",
               "block": { "kind": "service", "name": "billing", "part": null, "id": "service:production/billing" },
               "environment": "production",
               "lastRun": { "startedAt": "2026-11-26T02:00:00Z", "durationSeconds": 30.0, "status": "failed" },
               "runs": 3, "failed": 1, "successRatio": 0.667, "usualSeconds": 30.0,
               "recent": ["succeeded", "succeeded", "failed"], "running": false } ] }
```

- **One row per job.** A job is its source, block, environment, and name. Jobs whose latest run failed come first,
  then those running, then by name. A job is listed when a run of it started in the window, or when SQL Server's
  agent says it runs now.
- **`id`.** Opaque and safe in a URL, the same every time: the job's source, environment, block, and name.
- **`block`.** In the shape the alert list gives its blocks: a traced job's service, or a SQL Server Agent job's
  database server (`database`, its engine and server as `name`).
- **`lastRun`.** The latest run of the window; for a job running now, `status` `running`, `startedAt` when it started,
  and `durationSeconds` `null`. `null` when the job has neither.
- **`runs`, `failed`, `successRatio`.** Runs that started in the window, those that failed (failed, retried, or
  cancelled), and the share of finished runs that succeeded, `null` without any.
- **`usualSeconds`.** The median duration of the window's runs that succeeded, `null` without any, so failures that
  end early do not pull it down.
- **`recent`.** The statuses of the latest 20 runs, oldest first, for a run strip; a run going on now is last.
- **`figures`.** What every listed job adds up to before the `status` filter, so the screen's status tabs keep their
  numbers: `jobs`, `runs`, `failed`, `successRatio`, and how many jobs are `running`.

### `GET /api/v1/jobs/{id}`

One job by the `id` the list gave, with the same window. `404` for an ID that is not a job's, or a job of which no run
is kept and that does not run now.

```json
{ "from": "2026-11-25T12:00:00Z", "to": "2026-11-26T12:00:00Z",
  "job": { "id": "WyJ0cmFj...", "name": "nightly-import", "...": "as on the list" },
  "figures": { "runs": 3, "failed": 1, "successRatio": 0.667, "medianSeconds": 30.0, "p95Seconds": 30.0,
               "recordsProcessed": 600 },
  "durations": [ { "at": "2026-11-26T00:00:00Z", "seconds": 30.0, "status": "succeeded" } ],
  "runs": [ { "runId": "4bf92f3577b34da6a3ce929d0e0e4736", "startedAt": "2026-11-26T02:00:00Z",
              "durationSeconds": 30.0, "status": "failed", "message": "Deadlock victim",
              "traceId": "4bf92f3577b34da6a3ce929d0e0e4736", "traceKept": true,
              "recordsProcessed": 200, "recordsTotal": 250, "retries": 1,
              "steps": [ { "name": "extract", "durationSeconds": 10.0, "status": "succeeded", "message": null },
                         { "name": "load", "durationSeconds": 20.0, "status": "failed",
                           "message": "Deadlock victim" } ] } ] }
```

- **`job`.** The job as the list shows it. Without runs in the window, `runs` is `0` and `lastRun` is its latest run
  from before the window.
- **`figures`.** Over every run of the window: `medianSeconds` and `p95Seconds` of the finished runs, `null` without
  any; `recordsProcessed` added up, `null` when no run says.
- **`durations`.** Each run of the window, oldest first, for the duration chart; at most 5,000, the newest.
- **`runs`.** The window's runs, newest first, at most 200. `runId` is `cicd.pipeline.run.id` or the trace ID for a
  traced run, and the job history's instance ID for SQL Server Agent. `message` is the root span's status message or
  exception, or the job history's message. `traceId` is `null` for SQL Server Agent; `traceKept` says whether the
  trace is still stored, since traces are kept 7 days and runs 30. `recordsProcessed`, `recordsTotal`, and `retries`
  are what the root span says (see [Jobs and pipelines](../integrations/jobs.md)), `null` when it says nothing; for
  SQL Server Agent, `retries` counts the steps the job history marks as retried.
- **`steps`.** A traced run's steps are the root span's direct children in start order, at most 100, kept when the root
  arrived; a child that arrives after its root is not among them. A SQL Server Agent run's steps are read from its job
  history, kept 90 days, as the server's Jobs tab reads them. `status` is `succeeded`, `failed`, `running`, or
  `unknown`.

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
| `block` | none | Only alerts about the block of exactly this name, as on a block's own screen (VEY-68) |
| `part` | none | Only alerts about exactly this part: a disk, a database, or a container's host (VEY-68) |
| `order` | `newest` | `newest` first by the time each opened, or `worst`: warnings first, each the oldest first (VEY-68) |
| `limit` | `50` | The most alerts on the page, 1 to 200 |
| `cursor` | none | The `next` of the previous page |

```json
{
  "items": [{ "id": "…", "rule": "disk-filling", "environment": "production",
              "block": { "kind": "host", "name": "web-01", "part": "/", "id": "host:production/web-01" },
              "severity": "Caution", "worst": "Caution", "value": 9.2, "peak": 9.2,
              "peakAt": "2026-11-10T07:30:00Z", "threshold": 14, "windowSeconds": 86400,
              "openedAt": "2026-11-10T03:20:00Z", "updatedAt": "2026-11-10T07:34:00Z", "resolvedAt": null,
              "area": null, "incidentId": "0193a2b4-…", "incidentKey": "INC-42" }],
  "next": "MTc2Mjc0NDgwMDAwMDAwMDA6MDE5M2EyYjQ…"
}
```

- `incidentId` and `incidentKey` name the incident the alert belongs to (ADR 0026), so screens can link it; both are
  `null` for alerts opened before Veyra kept incidents.

- `value`, `peak`, and `threshold` are in the rule's unit: a ratio from 0 to 1 for `error-rate` and `disk-space`,
  seconds for `latency-p95`, and days until full for `disk-filling`, where 365 means not filling. `peak` is the worst
  measurement: the highest, or for days until full the fewest.
- `severity` is the severity now, or the last one of a resolved alert; `worst` the highest it ever had.
- `area` is, for a database server's alert, the area of its screen it belongs to, such as `blocking` (VEY-68);
  `null` for other blocks.
- `block` and `part` are kept in the read, before the page's limit, so a block's own screen gets exactly its alerts
  however many others hold its name. The cursor of a `worst` page continues in that order.
- `next` is `null` on the last page. An unknown rule, state, or kind, a limit or a number of days out of range, and a
  cursor Veyra did not make answer `400`, as does an order other than `newest` or `worst`.

## A database server's areas: `GET /api/v1/alerts/areas`

How each area of a database server's screen is from its open alerts, for its Overview tab (VEY-62, VEY-68). `block`
is the server's block, such as `microsoft.sql_server sql-prod-01`; `area` names the areas the screen shows, in the
order of its tabs, repeated, or every one when left out: `live`, `queries`, `waits`, `blocking`, `databases`, `jobs`,
`events`, `server`.

```json
[{ "area": "blocking", "health": "Warning", "alerts": [{ "rule": "db-head-blocker", "…": "…" }] },
 { "area": "live", "health": "Ok", "alerts": [] }]
```

The worst area comes first; areas alike keep the order asked for. `health` is `Warning`, `Caution`, or `Ok` from the
area's worst open alert, and `alerts` are its open alerts, the warnings first, each as in the list above. Every open
alert of the server is read, page by page. A missing `block` or an unknown `area` answers `400`.

## Catalog rules: `GET /api/v1/alerts/catalog`

The catalog rules (ADR 0020) with what screens need to word their alerts: `id`, `name`, `description`, `unit`
(`seconds`, `ratio`, `count`, `bytes`, `days`, `flag`), `noun`, `headline` and `recovery` with `{server}`, `{part}`,
and `{value}` to fill in, `partLabel`, `lowerIsWorse`, and the installation's `enabled`, `caution`, and `warning`.
`meaning` says what an alert of the rule means and why it matters, and `steps` what to do, in order: each with its
`text` and, where there is one, a `script` to run. Both have `{server}` and `{part}` to fill in; in a script, `{part}`
is filled in so that it is safe inside `N'…'`, and `[{part}]` as a bracketed name.

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

## Look: `PUT /api/v1/account/look`

The look of the web app the signed-in person picked (VEY-69, ADR 0021), kept on the account so it follows them to
every device. The looks are `forecast` and `console`.

```json
{ "look": "console" }
```

- `null` clears the choice, so the default look is used again.
- `204` when kept; `400` with a problem when the look is not one of the looks; `403` with a problem when an Admin
  set one look for everyone.

Every answer with the signed-in user, from `GET /api/v1/session` and from each step of signing in, has `look`:

```json
{ "look": { "chosen": "forecast", "default": "console", "mayChoose": true } }
```

| Field | Meaning |
| --- | --- |
| `chosen` | The look the person picked, or `null`. It stays saved while an Admin has set one look for everyone |
| `default` | The look for everyone who has not picked one: `Veyra:Access:DefaultLook` on the Settings screen, `console` unless an Admin saved another (ADR 0029) |
| `mayChoose` | Whether people may pick their own look: `Veyra:Access:PeopleChooseLook` |

The app shows `chosen` when `mayChoose` is true and a look is chosen, and `default` otherwise. A change to either
setting applies at once, without a restart.

## Saved views: `/api/v1/views`

Named queries of a screen (ADR 0024), kept per person beside their look, for any signed-in person. The only screen
with views is `logs`. A view holds the query text, never the time range: opening one keeps the range on screen.

`GET /api/v1/views?screen=logs` answers the person's own views first, then the views others shared, each by name:

```json
{
  "items": [
    {
      "id": "0193f1c2-…",
      "screen": "logs",
      "name": "Slow payments",
      "query": "service:payments AND level>=warning \"timed out\"",
      "shared": true,
      "mine": true,
      "ownerName": "Ana",
      "createdAt": "2026-11-26T09:00:00+00:00",
      "updatedAt": "2026-11-26T09:05:00+00:00"
    }
  ]
}
```

- `mine` says the person owns the view and may change or delete it; `ownerName` is who keeps it, so a shared view
  says who shared it. Others' views that are not shared are never listed.
- `POST /api/v1/views` takes `{ "screen": "logs", "name": "…", "query": "…", "shared": false }` and answers `201`
  with the view and its `Location`.
- `PUT /api/v1/views/{id}` takes `{ "name": "…", "query": "…", "shared": true }` and answers `200` with the view; its
  screen stays.
- `DELETE /api/v1/views/{id}` answers `204`.
- **Rules.** `name` is 1 to 80 characters, without the spaces around it; `query` is at most 2,000 characters, and
  empty or missing for all records. The query is kept as written; the logs endpoints check it when it is used.
- **Errors**, each with a problem body: `400` for an unknown screen (also on `GET`), a name, or a query out of
  bounds; `409` for a person who keeps 100 views already; `403` for changing or deleting a view someone else shared;
  `404` for a view the person does not see, whether it is someone else's private view or none at all.

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

- `told` lists the channels the alert told, each once, in the order they were first told, as
  `[{ "id": "…", "name": "#ops-alerts" }]` (VEY-68).
- `incidentId` and `incidentKey` name the alert's incident, as on the list; `null` when it belongs to none.
- Each rule fills its own part and leaves the others `null` (or `topErrors` empty): `series` and `topErrors` for
  `error-rate`, `latency` for `latency-p95`, `disk` for `disk-space` and `disk-filling`, and `serverErrors` for
  `db-severe-error`, `db-memory-error`, and `db-login-failures`.
- `serverErrors` lists the errors the rule counted, from 15 minutes before the alert opened to its end or now, newest
  first, each with `at` (when the server was read with it), `error`, `severity`, and `message` (VEY-61).
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

## Incidents: `GET /api/v1/incidents`

The Incidents screen's list (ADR 0026): every incident that was open at some time in the last days, the open and
acknowledged ones first, then the resolved ones, each the newest first, a page at a time. An incident groups related
alerts; see [`docs/architecture/alerting.md`](../architecture/alerting.md#incidents).

| Parameter | Default | Meaning |
| --- | --- | --- |
| `state` | `all` | `open`, `acknowledged`, `resolved`, or `all` |
| `days` | `30` | How many UTC days back, today counting as one; 1 to `Veyra:Alerting:HistoryDays` (90) |
| `environment` | all | Only incidents of this environment |
| `limit` | `50` | The most incidents on the page, 1 to 200 |
| `cursor` | none | The `next` of the previous page |

```json
{
  "items": [{ "id": "0193a2b4-…", "number": 42, "key": "INC-42",
              "title": "Container shop-api on web-01 uses 95% of its memory limit",
              "environment": "production", "severity": "Warning", "state": "acknowledged",
              "openedAt": "2026-11-26T11:40:00Z", "acknowledgedAt": "2026-11-26T11:44:00Z",
              "acknowledgedBy": "Ana", "resolvedAt": null, "alertCount": 2,
              "blocks": [{ "kind": "host", "name": "web-01", "part": null, "id": "host:production/web-01" },
                         { "kind": "container", "name": "shop-api", "part": "web-01",
                           "id": "container:production/shop-api" }] }],
  "next": null
}
```

- `title` is the headline of the incident's first alert, as notifications word it, or of a worse alert that joined
  later. `severity` is the worst of its alerts.
- `blocks` are the blocks its alerts are about, in the alert list's shape, each once, in the order their alerts joined.
  They are whole blocks: two disks of a host are one block, and `part` is set only for a container, to its host.
- An unknown state, a limit or a number of days out of range, and a cursor Veyra did not make answer `400`.

## One incident: `GET /api/v1/incidents/{id}`

The incident screen and the link in every notification of its alerts. Unknown IDs, and incidents whose alerts were
all deleted after `Veyra:Alerting:HistoryDays`, answer `404`.

```json
{
  "incident": { "id": "0193a2b4-…", "key": "INC-42", "…": "as in the list" },
  "alerts": [{ "id": "…", "rule": "disk-space", "…": "as GET /api/v1/alerts lists them" }],
  "figures": { "firstDetectedAt": "2026-11-26T11:40:00Z", "durationSeconds": 1260, "affectedBlocks": 2,
               "leading": { "alertId": "…", "rule": "container-memory", "value": 0.95, "threshold": 0.9,
                            "peak": 0.95, "peakAt": "2026-11-26T11:50:00Z" } },
  "timeline": [{ "at": "2026-11-26T11:40:00Z", "kind": "opened", "alertId": "…", "rule": "disk-space",
                 "block": { "kind": "host", "name": "web-01", "part": "/", "id": "host:production/web-01" },
                 "severity": "Caution", "value": 0.92, "actor": null, "deliveries": [] },
               { "at": "2026-11-26T11:44:00Z", "kind": "acknowledged", "alertId": null, "rule": null,
                 "block": null, "severity": null, "value": null, "actor": "Ana", "deliveries": [] }],
  "runbooks": [{ "rule": "disk-space", "url": "https://wiki.example.com/runbooks/disk" }]
}
```

- `alerts` run in the order they joined; `durationSeconds` runs to the incident's end, or to now while it is open.
- `leading` is the alert with the worst severity it ever had, the first to open of those: its latest `value`, the
  `threshold` of its current severity, and its `peak`, in the rule's unit.
- `timeline` runs oldest first. `kind` is `opened`, `escalated`, `eased`, or `resolved` for an alert's event, with its
  notifications in `deliveries` as on the alert details; `acknowledged` or `unacknowledged` for the incident, with who
  did it in `actor`.
- `runbooks` lists only the rules of the incident that have a link set on the Settings screen
  (`Veyra:Alerting:Runbooks:{rule}`); a rule without one shows Veyra's own guidance.

## Acknowledging: `POST` and `DELETE /api/v1/incidents/{id}/acknowledge`

Editors and Admins (ADR 0008), with the antiforgery header. `POST` records that the signed-in person is on the
incident: the state becomes `acknowledged`, with `acknowledgedAt` and `acknowledgedBy` (their display name), and the
timeline gets an `acknowledged` moment. `DELETE` takes it back: the state is `open` again and the timeline gets an
`unacknowledged` moment. Both answer `200` with the incident as the list shows it; doing what is already done changes
nothing. A resolved incident answers `409`, an unknown one `404`, a Viewer `403`. Acknowledging does not stop
notifications.

## What surrounds an incident: `GET /api/v1/incidents/{id}/context`

Served by the Query module from the incident the Alerting module keeps (ADR 0026). Every read stays inside the
window, from 30 minutes before the incident opened to 10 minutes after it resolved, or now, and inside the affected
blocks. Unknown IDs answer `404`.

```json
{
  "from": "2026-11-26T11:10:00Z", "to": "2026-11-26T12:01:00Z",
  "executionPath": { "source": "trace", "traceId": "0af7651916cd43dd8448eb211c80319c", "jobName": null,
                     "steps": [{ "name": "POST /pay", "service": "payments-gateway", "durationMs": 200,
                                 "status": "error", "detail": "payment failed" }] },
  "exception": { "type": "CardDeclinedException", "message": "The card was declined", "stackTrace": "at Charge()",
                 "service": "payments-gateway", "traceId": "0af7651916cd43dd8448eb211c80319c", "source": "trace" },
  "timeline": [{ "at": "2026-11-26T11:39:12Z", "kind": "first-error",
                 "block": { "kind": "service", "name": "payments-gateway", "part": null,
                            "id": "service:production/payments-gateway" },
                 "title": "First error of payments-gateway", "detail": "Payment failed" }],
  "charts": [{ "title": "Error rate", "block": { "…": "…" }, "unit": "ratio",
               "lines": [{ "label": "errors", "latest": 0.25,
                           "points": [{ "at": "2026-11-26T11:10:00Z", "value": null }] }] }],
  "logs": { "items": [{ "…": "as GET /api/v1/logs returns them" }],
            "query": "service:payments-gateway AND env:production AND level>=warning", "total": 214 },
  "components": [{ "block": { "kind": "service", "name": "payments-gateway", "part": null,
                              "id": "service:production/payments-gateway" },
                   "health": "warning", "inIncident": true, "relation": "affected" }]
}
```

- `executionPath` is, for an affected service, the critical path of a failing trace of the window, root first: the
  newest failing traces are opened, at most three, and the first whose cause has an exception wins. For a
  `db-agent-job` alert without such a trace, it is the steps of the job's latest failed run in the job runs (ADR 0027;
  a run from before them is found in the job history), with its steps from the job history (`source` `job`,
  `jobName` set, `service` null, `durationMs` from the run's history). `null` when neither was found. `status` is
  `ok`, `error`, or `unset`.
- `exception` is the trace's cause: the failed span none of whose children failed, read from its `exception` event
  (type, message, stack trace), or from the span's attributes. Without one it is the most frequent error message of
  the affected services in the window (`source` `logs`), with the type and stack trace of a record of it that carries
  `exception.type` and `exception.stacktrace`. `null` when there is neither.
- `timeline` holds, oldest first, the failed SQL Agent job runs (`job-failed`), setting changes (`config-change`,
  detail `old → new`), and deadlocks (`deadlock`) of the affected database servers, and the first error record of
  each affected service (`first-error`).
- `charts` holds at most 4, the first affected block's first: a service's error rate from its logs (`ratio`) and p95
  latency (`ms`); a host's CPU busy and memory used (`ratio`); a container's CPU (`percent`) and memory (`bytes`); a
  database server's connections and blocked sessions (`count`) and, for SQL Server, its waits (`ms` waited per
  second, per category). Each line has `label`, `latest`, `average`, `highest`, and a point per bucket of the window; a
  block without data draws none.
- `logs` holds the newest 50 records at Warning or worse of the affected services, hosts, and containers, `query` the
  log query (ADR 0024) that shows all of them on the Logs screen with the same window, and `total` how many match;
  without such blocks, `items` is empty, `query` empty, and `total` `null`.
- `components` lists the affected blocks (`affected`), then the hosts they run on (`host`), then what runs on an
  affected host (`runs-on-host`), each once, as the series' `host.name` says. `health` is the worst open alert of the
  block now, or `silent` when it stopped sending for longer than the Query settings allow, or `ok`. `inIncident` is
  whether the block has an open alert of the incident.

## What surrounds an alert: `GET /api/v1/alerts/{id}/context`

Served by the Query module from the alert the Alerting module keeps (ADR 0029), with the parts of the incident
context and their shapes. The window runs from an hour before the alert opened until it resolved, or now. Unknown IDs
answer `404`; any signed-in role may read it.

```json
{
  "from": "2026-11-26T10:40:00Z", "to": "2026-11-26T12:01:00Z",
  "logs": { "items": [{ "…": "as GET /api/v1/logs returns them" }],
            "query": "service:payments-gateway AND service:web-01 AND env:production AND level>=warning",
            "total": 214 },
  "components": [{ "block": { "kind": "service", "name": "payments-gateway", "part": null,
                              "id": "service:production/payments-gateway" },
                   "health": "warning", "inIncident": true, "relation": "affected" },
                 { "block": { "kind": "host", "name": "web-01", "part": null, "id": "host:production/web-01" },
                   "health": "ok", "inIncident": false, "relation": "host" }]
}
```

- `components` lists the alert's block (`affected`, `inIncident` true, also once the alert resolved), then the
  blocks related to it as for an incident: the hosts it runs on (`host`) and, for a host, what runs on it
  (`runs-on-host`). `health` is each block's state now, as in the incident context.
- `logs` holds the newest 50 records at Warning or worse of those blocks in the window, except database servers,
  which write no log records, with `query` and `total` as in the incident context.

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

The page also counts the alert rules, each switched by its `On` setting, as `rules`, and those switched on now, as
`rulesOn` (VEY-68), for the Rules line of the section list.

`GET /api/v1/settings/listing` says which settings and sections the screen lists (VEY-67); the page above stays the
model it edits. `search` keeps the settings whose label, card, help, or key hold every word, in any section and
view; without it, `section` (such as `Data`) lists its basics in the Simple view, or all of it in `view=advanced`, or
all of it when it has no basics. It answers `{ "keys": ["…"], "sections": ["Data", "Rules", "…"] }`: the keys in
screen order, and the sections of the list: those with settings in the view, `Databases` and `Agents`, and `System`
in the Advanced view. A `view` other than `simple` or `advanced` is `400`.

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
  "encryption": "Required",
  "instance": null,
  "authentication": "sql",
  "modules": [{ "id": "activity", "enabled": false, "intervalSeconds": 10 }]
}
```

- `name` is the server's database block: letters, digits, dots, dashes, and underscores, at most 63, unique. A
  taken name answers `409`; a broken rule answers `400` with the reason as the problem's title.
- `agentId` null means the built-in agent. `encryption` is `Required`, `TrustServerCertificate`, or `Off`.
- The password is stored encrypted and never answered. `PUT` with `"password": null` keeps the stored one.
- `port` may be left out for 1433. `instance` names a named instance (ADR 0028), reached through the SQL Server
  Browser instead of the port; `address` may also be `HOST\INSTANCE`, which is split into the two. Only a SQL Server
  its owner runs has one.
- `authentication` is `sql` (the default), `ntlm` (a Windows login as `DOMAIN\name` with its password), or
  `integrated` (the account the agent's service runs as: no `login` or `password`, and only for an agent that reports
  Windows). Windows authentication is for `platform` `SqlServer` only. A SQL login with a backslash answers `400`.
- `modules` switches and times the template's modules (see `GET /api/v1/integrations/modules/sqlserver`). Those left
  out keep what the server has, their defaults for a new one; `null` changes none. `core` cannot be switched off, and
  an interval must be one of the module's `choices`.
- Each server in an answer also has `instance`, `authentication`, `modules` (every module, in order), and `version`,
  the template version it is read with. A new server takes the current version.
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
- `GET /api/v1/agents/summary` counts them for the Settings list (VEY-68): `{ "sqlServers": 2, "postgresServers": 1,
  "needLook": 1, "agents": 2, "agentsOnline": 1 }`. `needLook` counts the servers whose state is `AgentOffline`,
  `ConfigurationFailed`, `LoginFailed`, `Unreachable`, `CertificateRejected`, `GrantMissing`, or `Failing`.

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

## Integrations: `/api/v1/integrations`

Administrators only, because every answer names a login or a connection and probes carry passwords (ADR 0008,
ADR 0028). A monitored SQL Server is an integration: the servers of `/api/v1/sql-servers`, with their template version,
modules, and package page. Passwords go in and never come back.

`GET /api/v1/integrations` answers `{ "items": [ … ] }`, each item:

```json
{
  "id": "0195…", "kind": "sqlserver", "name": "sql-prod-01", "environment": "production",
  "agent": { "id": "0195…", "name": "Built in" },
  "version": "0.2.0", "latestVersion": "0.2.0", "state": "Receiving",
  "modules": [{ "id": "core", "enabled": true, "intervalSeconds": 15 }],
  "createdAt": "2026-11-26T09:00:00Z", "changedAt": "2026-11-26T09:00:00Z"
}
```

`state` has the words of the SQL Servers list. `GET /api/v1/integrations/modules/sqlserver` answers the modules in
order: `[{ "id": "core", "name": "Core", "description": "…", "defaultIntervalSeconds": 15, "choices": [10, 15, 30, 60],
"required": true }]`. The ids are `core`, `activity`, `queries`, `storage`, `backups`, `jobs`, `events`,
`configuration`, and `indexes`; see [the SQL Server guide](../integrations/sqlserver.md#modules-and-intervals).

**The package page.** `GET /api/v1/integrations/sqlserver/{id}` takes `range`, `from`, and `to` like every screen
(ADR 0022; `400` past 31 days, `404` for no such server) and answers:

```json
{
  "integration": { "…": "the item above" },
  "package": {
    "name": "veyra-sqlserver-integration", "version": "0.2.0", "latestVersion": "0.2.0", "previousVersion": "0.1.0",
    "setAt": "2026-11-26T09:00:00Z", "setBy": "Dana Kim", "collectorVersion": "0.140.1",
    "collectorCompatibility": "otelcol-contrib 0.140", "minimumSqlServer": "SQL Server 2019",
    "login": "veyra_monitor", "authentication": "sql", "loginLacks": ["VIEW SERVER STATE"]
  },
  "collector": {
    "agentId": "0195…", "agentName": "Built in", "healthy": true, "startedAt": "2026-11-26T06:00:00Z",
    "uptimeSeconds": 10800, "lastSeenAt": "2026-11-26T09:00:00Z", "configState": "applied", "configError": null
  },
  "validation": { "lastProbe": null },
  "modules": [{ "id": "core", "name": "Core", "enabled": true, "intervalSeconds": 15, "series": 214,
                "points": 51360, "line": [856, 856, null] }],
  "checks": [{ "name": "sessions", "module": "activity", "secondsPerRun": 0.012, "runs": 2160, "ok": true }],
  "nextSteps": [{ "id": "channel", "text": "Add a notification channel, so alerts reach someone.", "done": false,
                  "link": "/notifications" }],
  "history": [{ "at": "2026-11-26T09:00:00Z", "by": "Dana Kim", "kind": "version", "from": "0.1.0", "to": "0.2.0" }]
}
```

- `previousVersion` is the version Roll back moves to, `null` when the server is on it. `setBy` is `null` for Veyra
  itself. `login` is `null` for integrated authentication. `loginLacks` is what the permissions read reported missing
  in the last 15 minutes.
- `collector` comes from the agent's OpAMP reports; `startedAt` is its health's start time. `configState` is
  `applied`, `applying`, `failed`, or `unknown`.
- `validation.lastProbe` is the server's latest probe of the last day, shaped like `GET …/probes/{id}`.
- `modules` counts each module's metric series and points over the window, and its points per bucket in `line` (60
  buckets, `null` for none). A metric belongs to the module whose receiver's queries name it; the `sqlserver`
  receiver's own metrics are Core's. Modules whose reads send records rather than metrics, such as Activity or
  Queries, show no series.
- `checks` lists the reads the agent runs for the server, by their `/* veyra-agent: name */`, with SQL Server's time
  per run and runs over the window from the statement statistics marked `agent`; both are `null` for a read the
  statistics did not list. `ok` is false while the read's receiver logs errors.
- `nextSteps` are `thresholds` (an Admin saved a database rule setting), `channel` (a notification channel is on), and
  `retention` (an Admin saved a retention setting), with the screen that does each.
- `history` lists the latest 20 changes of the server in the settings history; `kind` is `added`, `changed`,
  `removed`, or `version`, with `from` and `to` for a version.

**Update and roll back.** `POST /api/v1/integrations/sqlserver/{id}/version` takes `{ "version": "0.1.0" }`, one of the
two versions Veyra ships, and answers the item; another version answers `400`. The agent reads with it from its next
poll, and the settings history records the change with both versions.

**Probes.** `POST /api/v1/integrations/probes` tests a connection through an agent before it is saved (Test
connection) or a saved server's (Validate now):

```json
{ "agentId": null, "serverId": null, "platform": "SqlServer", "address": "sql01.corp.local", "port": 1433,
  "instance": null, "authentication": "sql", "login": "veyra_monitor", "password": "…", "encryption": "Required",
  "database": null }
```

- It answers `202` with `{ "id": "0195…" }`. The connection follows the rules of `/api/v1/sql-servers` (`400`
  otherwise). `agentId` null means the saved server's agent, or else the built-in one.
- With `serverId` and no `address`, the server's stored connection and password are tested; with an `address`, the
  connection given, with the stored password when `password` is left out. An unknown `serverId` answers `404`.
- `GET /api/v1/integrations/probes/{id}` answers `{ "id", "state", "startedAt", "finishedAt", "error", "result" }`.
  `state` is `pending`, `succeeded`, or `failed`. `error` is `{ "reason", "message" }` with `reason` one of `no-agent`,
  `agent-offline`, `unreachable`, `login-failed`, `certificate`, `timed-out`, or `other`. `result` is
  `{ "version", "updateLevel", "edition", "platform", "databases", "databaseNames", "missingPermissions" }`, with at
  most 20 database names. A probe is kept a day; after that it answers `404`.
- A probe takes 30 to 60 seconds: the agent picks it up with its next poll, reads once, and Veyra removes it when the
  answer arrives or after 2 minutes (`timed-out`). The agent's Collector applies its configuration twice, which pauses
  its other reads for a few seconds each time. The probe's password is encrypted while it runs and deleted when it
  finishes. `no-agent` and `agent-offline` fail at once.

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
  "events": [{ "at": "2026-07-02T10:00:00Z", "kind": "RoleChanged", "by": "Dana Kim", "detail": "Viewer Editor" }],
  "signInDays": { "days": [{ "day": "2026-10-28", "signedIn": 0, "failed": 0 }], "signedIn": 2, "failed": 64 }
}
```

- **`GET /api/v1/users/{id}`** above. It takes `timeZone`, the viewer's IANA zone (UTC when left out; `400` when
  unknown). `signIns` lists the latest 50 attempts; `signInDays` counts every attempt of the last 14 days per day on
  the viewer's calendar, the oldest first and today last, with their totals (VEY-68), so the chart counts them all.
- **`GET /api/v1/users`** takes `search` (a name, email, or team, in any case) and `show` (`everyone`, the default,
  `nofactor`, `locked`, or `off`; any other value is `400`), and answers `{ "people": [...], "counts": {...},
  "posture": {...}, "roles": {...} }` (VEY-67). `people` has the `user` part of each person the two keep, by email.
  `counts` (`everyone`, `noFactor`, `locked`, `off`) says how many of the search's matches each chip lists.
  `posture` counts everyone, whatever the search: `people`, `withSecondFactor`, `recentSignIns` (within 30 days),
  active `admins` and `adminsWithSecondFactor`, and `missing` and `missingAdmins`, who can sign in without a second
  factor. `roles` counts the `admins`, `editors`, and `viewers`.
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
