# 0029. Console traces, metrics, alerts, and settings; Console the default look

Date: 2026-11-26
Status: Accepted

## Context

The last step of the Console look (VEY-69 step 8, VEY-79) draws the screens it still borrows from the shared features:

- Traces and one trace.
- Metrics.
- One alert.
- Settings.

ADR 0021 already says Console becomes the default look once it is complete.

The mockups on VEY-79 show figures the server does not answer yet. Every filter, order, and figure comes from the server
(VEY-67, VEY-68).

- **Traces.** Worst first: failed, then slowest.
  - The traces list is newest first only.
  - The services read counts traces per service, but has no p95 or failed share.
  - No read answers the range's figures: traces, failed, slower than p95, median, services.
- **Metrics.** The explorer shows a table of the chart's lines with their latest, average, and highest values.
  - The chart's lines carry only points.
  - The chart read accepts a service and a host, but the screen never sends them.
- **One alert.** The page shows the blocks the alert touches, with their state, and the logs around its opening.
  - Both exist only for incidents (ADR 0026), in `GET /api/v1/incidents/{id}/context`.

## Decision

### 1. Traces

- **The order.** `GET /api/v1/traces` takes `order=newest|worst`, `newest` by default.
  - `worst` puts failed traces first, then the slowest.
  - Its cursor carries failed, duration, and trace ID, so a page continues where the last one ended.
- **Services.** `GET /api/v1/traces/services` answers, per service, its traces, p95, and failed traces in the range under
  the same filters. It is ordered slowest p95 first.
- **Figures.** `GET /api/v1/traces/summary` answers, under the same filters:
  - the traces, the failed, and those at least as slow as the range's p95, which `slow=true` lists
  - the median and p95
  - the services
  - the traces of the period before, for the change
- **One trace.** The existing read. Its figures (duration, spans, services, errors, and the critical path's share) come
  from the trace itself.

### 2. Metrics

- **Each line's figures.** Each line of `GET /api/v1/metrics/chart` carries its latest, average, and highest value, read
  with the points.
- **Filters.** The explorer sends `service` and `host` to the chart read.

### 3. One alert

- **The read.** `GET /api/v1/alerts/{id}/context`, in the Query module beside the incident context, answers:
  - the affected blocks: the alert's block and its related blocks (ADR 0026), each with its state now
  - the logs around the alert: warnings and worse from those blocks, from an hour before it opened until it closed or
    now, newest first, at most 50, with the total
- **What it reads.** The alert through a reader in `Veyra.Core`, as the incident context does, so Query does not
  reference Alerting.

### 4. Settings

- **No new server work.** Console draws the shared settings, sections, search, review, history, export, and import in
  its own frame: section navigation, panels with rows, and a sticky save bar.
- **Looks offered.** Only looks that exist are shown.

### 5. Console as the default look

- **The setting.** `Veyra:Access:DefaultLook` defaults to Console.
  - An installation whose Admin never set it moves to Console at upgrade.
  - An installation that set Forecast keeps it, and so does a person who picked Forecast.
- **The screen.** The frontend falls back to Console when the session names no default.
- **The tests.** The browser tests that run every viewport run Console (ADR 0021); Forecast keeps its own specs.

## Alternatives considered

- **Sort and count traces in the browser.** It needs no new reads, but it breaks the rule that the server filters,
  orders, and counts.
- **Line figures computed from the points in the browser.** The same rule; the points are also cut to a bucket size.
- **Reuse the incident context for an alert.** An alert without an incident would have no context, and most alerts
  have none.
- **Keep Forecast as the default until each installation chooses.** Every installation would have to act to see the
  new look, which the owner asked to be the default.

## Consequences

- Worst-first order and the summary read the range's traces; they need indexes that keep them within the speed budgets
  (VEY-28).
- Installations that never chose a default look change look at upgrade; the release notes say so.
- Forecast stays complete and selectable. Both looks keep their own browser tests.
