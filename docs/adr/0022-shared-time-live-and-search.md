# 0022. One time range, live updates, search across signals, and resource lists

Date: 2026-11-26
Status: Accepted

## Context

The Console look (ADR 0021, VEY-69) has a top bar like the reference screenshots:

- one time range for every screen
- a refresh interval
- Live, with events per second
- a search that finds services, hosts, databases, traces, and logs

Its menu also has Resources: Services, Hosts & containers, and Databases, each a list of everything Veyra knows,
worst first.

Today each screen keeps its own window and timer:

- Logs and traces take any `from`/`to` up to 31 days.
- Metrics take only 1h, 6h, 24h, or 7d.
- Alerts take a number of days.
- The home is fixed at the last 5 minutes and 24 hourly buckets.

Each hook polls on its own timer, and nothing is pushed. Search exists per screen only. Hosts are known only while
they report, so a machine that stops is no longer listed. No figure is compared with the period before it.

Retention bounds what a range can show:

| Signal | Kept by default | Counted ahead |
| --- | --- | --- |
| Logs | 14 days | per minute and per hour, by environment, service, and severity (ADR 0015) |
| Metrics | 30 days | one point per series per hour (ADR 0015) |
| Traces | 7 days | none |

Every figure, filter, and ranking is worked out on the server (VEY-66 to VEY-68), and every query stays within its
speed budget (ADR 0015).

## Decision

### 1. One time range

- **The range is part of the shell contract (ADR 0021).** `ShellContext.time` gives every screen the same `from`
  and `to`, the preset that made them, and whether Live is on.
- **Where it is kept.** It lives in the address (`?range=24h`, or `?from=…&to=…` for a custom range), so a shared
  link shows the same window.
- **Presets.** Last 15 minutes, 1 hour, 6 hours, 24 hours, 7 days, and 30 days, plus a custom from–to.
- **How each look shows it.** Console has one picker in its top bar. Forecast keeps each screen's own picker, which
  sets the same shared range. A screen never keeps a window of its own.
- **When a range is longer than a signal is kept,** the screen shows what is kept and says so, for example "Traces
  are kept 7 days".

### 2. Every endpoint takes `from` and `to`

- **The parameters.** Every list, series, and summary endpoint accepts `from` and `to` (ISO 8601, UTC).
- **The step.** The server chooses the step:
  - raw points or minutes up to 6 hours
  - hours beyond that, from the hourly counts and metric points that already exist
- **Metrics.** Metrics drop their fixed `range` list. `range` keeps working as a short way to write a window ending
  now.
- **The limit.** A window longer than 31 days is refused with a problem response, as logs already do.

### 3. Refresh and Live

- **Refresh.** The refresh interval (off, 10 s, 30 s, 1 min, 5 min; 30 s by default) is kept in the browser. The
  shell gives screens a `tick` that changes on each interval, and the screens' hooks fetch again on it. Their own
  timers go.
- **What Live does.** Live is a window that ends now and moves.
- **The stream.** `GET /api/v1/live?environment=` is a server-sent event stream (`text/event-stream`):
  - `rate`, every second, with events per second for logs, spans, and metric points over the last 10 seconds
  - `tick`, every 5 seconds, which tells screens to fetch again
- **Live log records.** `GET /api/v1/logs/stream` takes the same filters as `GET /api/v1/logs`. It sends each new
  record that matches, filtered on the server, in the order written.
- **Where the records come from.** Ingestion hands each written batch to a Core interface, `ILiveTelemetry`. The
  Query module serves the streams from it.
- **Bounds.** The feed is bounded like ingestion:
  - each listener has a queue of 1,000 records
  - when a listener falls behind, the oldest records go and a `skipped` event says how many
  - the number of listeners per instance is capped by a setting (`Veyra:Query:LiveListeners`, 50)
- **Back-pressure.** A slow browser never slows ingestion.
- **Why server-sent events.** They need no new dependency in .NET or the browser, pass through reverse proxies as
  ordinary HTTP, and reconnect by themselves. Traffic only flows one way, so WebSockets would add nothing.

### 4. Search across signals

`GET /api/v1/search?q=&environment=&from=&to=` answers groups in a fixed order:

| Group | What it holds |
| --- | --- |
| Services | Up to 5 names, with health and a short figure |
| Hosts and containers | Up to 5 names, with health and a short figure |
| Databases | Up to 5 servers or databases, with health and a short figure |
| Traces | Up to 5 root operations with their count |
| Logs | The number of records whose message holds the words |

- **Ranking.** Names rank exact match first, then prefix, then any match. The logs count uses the word search of
  ADR 0016.
- **Speed.** Each group has its own budget of 150 ms. A group that runs out is marked `partial` rather than holding
  up the others.
- **Empty search.** An empty `q` answers nothing.

### 5. Resource lists, and a known inventory

- **The endpoints.** `GET /api/v1/resources/services`, `/hosts`, and `/databases` answer one page of a list, worst
  first, with:
  - the figures for the header strip
  - the filters the person chose (health, kind, state, engine, a find box, and sort)
  - the total count
  - the next cursor
- **Silent things stay listed.** A host, container, or service that once reported stays in the inventory for as long
  as its metrics are kept. When nothing arrived for `Veyra:Query:SilentAfterHosts` (5 minutes, for hosts,
  containers, and database servers) or `Veyra:Query:SilentAfterServices` (2 hours, for services), it is shown as
  **Silent** and counted in "online x of y". So a dead machine is never missing.
- **Where things run.** Services map to the hosts they run on through `host.name` on their resources.

### 6. Comparisons

- **Change against the period before.** Header figures that change over time also give their change against the
  previous period of the same length. Rates are compared as percentages, ratios as percentage points.
- **Where it is worked out.** The server computes it in the same query. The browser only shows it.

### 7. Ownership

- **Query module:** the endpoints of points 4 to 6 and both streams.
- **Ingestion module:** reports through `ILiveTelemetry` in `Veyra.Core`.
- **Settings:** the three new settings (two silence times and the most live listeners) are edited on the Settings
  screen (ADR 0017).

## Alternatives considered

- **Each screen keeps its own window.** It is simple, but screens disagree about "now", and a link can't carry the
  window. The reference design has one range for everything.
- **WebSockets or SignalR for Live.** Both are two-way and need a hub, a new dependency, and proxy setup. Live only
  sends from the server.
- **Polling for Live.** It is what "Follow new" does today. It costs a query every few seconds per browser and can't
  show a true rate.
- **Search in the browser over lists it already has.** It breaks the rule that the server filters (VEY-66). It can
  only find what was loaded, and it can't count logs.
- **Hosts known only while they report.** This is today's behaviour, and it hides exactly the machine that died.

## Consequences

- Every screen reads the same window, and a link carries it.
- Screens fetch again on the shell's tick instead of keeping their own timers.
- Live shows the true rate and new records without polling. The server holds one open connection per listener,
  bounded by a setting.
- The inventory keeps silent hosts and services, so counts such as "11 of 12 online" are true.
- Every endpoint must accept `from` and `to`, and its tests cover the change of step at 6 hours and the 31-day limit.
