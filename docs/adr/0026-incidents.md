# 0026. Incidents: grouped alerts, acknowledge, runbooks, and what surrounds them

Date: 2026-11-26
Status: Accepted

## Context

The Console look's incident screen (VEY-69 step 5, reference screenshot 7) treats a failure as one thing to work on:

- **The incident.** An id (INC-0042), a title, and a state, with Acknowledge and View runbook.
- **Figures.** When it was first detected, what is affected, and how long it has lasted.
- **What happened.** The execution path that failed, the observed exception, and one timeline across signals.
- **Related charts.** For the affected blocks.
- **Related logs.** From around the incident.
- **Investigation context.** Possible causes, suggested checks, and the affected components with their state.

A survey of the code (VEY-76) found:

- **Alerts exist, incidents do not.** Alerts (ADR 0012) are kept per rule and block, with their events and
  deliveries (ADR 0009). Nothing groups them.
- **No acknowledgement.** Nothing acknowledges, though the Editor role promises it (ADR 0008).
- **No runbooks.** Every rule has guidance: what it means and how to fix it. There is no runbook link.
- **Relations are only inferred.** Relations between blocks come from `host.name` on metric series: a container's
  host, and the hosts a service or database server runs on. No endpoint lists them.
- **The pieces are spread out.** A trace's critical path and cause, job runs, SQL Server events, configuration
  changes, log counts, and metric series exist, each on its own screen.

## Decision

### 1. Incidents group alerts

- **Where they are kept.** The Alerting module keeps incidents:
  - `alerting.incident`: id, number (INC-n, a sequence), environment, title, severity (the worst of its alerts),
    state, opened, acknowledged (when and by whom), and resolved.
  - `alerting.incident_alert`: which alerts belong to which incident.
- **How an alert is grouped.** An alert that opens joins an open incident of the same environment when the incident
  has an alert on a related block that opened within the grouping window (15 minutes, a setting). Otherwise it opens
  a new incident.
- **What "related" means.**
  - the same block
  - a container and its host
  - a service or database server and a host it runs on, as its series say
- **Where relations come from.** Alerting never references Query. Relations come through a Core interface,
  `IBlockRelations`, which Query implements from the series it already reads.
- **The title.** The first alert's headline, worded as notifications word it (`NotificationMessages`). It is updated
  when a worse alert joins.
- **When it resolves.** An incident resolves when its last alert resolves.
- **History.** Resolved incidents are kept as long as alerts are (`HistoryDays`, 90).

### 2. Acknowledge

- **The endpoint.** `POST /api/v1/incidents/{id}/acknowledge`, for Editors and Admins (ADR 0008).
- **What it records.** Who acknowledged and when. It shows on the incident and its timeline.
- **What it does not do yet.** It does not stop notifications. Escalation policies and quiet periods are a later
  decision.
- **Taking it back.** `DELETE /api/v1/incidents/{id}/acknowledge` undoes it.

### 3. Runbooks

- **What a runbook is.** A rule may have a link to the installation's own runbook, set on the Settings screen under
  Alert rules (ADR 0017).
- **View runbook.** It opens that link.
- **Without a link.** It shows Veyra's built-in guidance, which every rule has.

### 4. The incident API

The Alerting module owns the incident itself:

- `GET /api/v1/incidents?state=&days=&environment=&limit=&cursor=`: the incidents, open first, then newest.
- `GET /api/v1/incidents/{id}`, which answers:
  - the incident
  - its alerts, as the alert list shows them
  - the figures: first detected, duration, affected blocks, and the leading alert's value against its threshold and
    peak
  - the timeline of its alerts' events with their deliveries, and the acknowledgement
  - the runbook link of each rule

The Query module owns what surrounds it. `GET /api/v1/incidents/{id}/context` reads the incident through a Core
interface, `IIncidentReader`, and answers:

- **Window.** From 30 minutes before the incident opened to 10 minutes after it resolved, or now.
- **Execution path.**
  - For a service, the critical path of a failing trace of the window, from `TraceView`, with each step's service,
    name, duration, and status.
  - For a SQL Server Agent job alert, the failing run's steps.
- **Observed exception.** The failing trace's cause, or the most frequent error message of the affected services with
  its stack trace when a record has one.
- **More on the timeline.**
  - failed job runs of affected database servers
  - their configuration changes and deadlocks
  - the first error record of each affected service
- **Related charts.** At most 4, chosen by block kind:
  - service: error rate and p95 latency
  - host and container: CPU and memory
  - database server: connections and blocked sessions, and waits
- **Related logs.** Warnings and worse from the affected blocks in the window, newest first, at most 50, with the
  log query that shows all of them.
- **Affected components.**
  - Each affected block, with its health now and whether it has an open alert of the incident.
  - Each related block that has none, so the screen can tell what failed from what only ran nearby.

### 5. Investigation context

- **Where it comes from.** The guidance every rule already has: what it means and its steps, from the catalog
  (ADR 0020) or the frontend's rule guidance.
- **How the screen shows it.** As possible causes and suggested checks, marked "not confirmed".
- **No new knowledge.** Veyra adds none here; the guidance stays in one place per rule.

### 6. The screens

- **The routes.** One registry (ADR 0021):
  - `incidents` at `/incidents`, in Respond
  - `incident` at `/incidents/:id`
- **Console.** It draws them as reference screenshot 7.
- **Forecast.** It gets a plain list and screen from the same feature, so every look can open an incident link sent
  by a notification.
- **Notification links.** They point at the incident when the alert belongs to one.

## Alternatives considered

- **One incident per alert.** Simpler, but a database outage that trips five rules would make five incidents, and
  the reference's grouping is the point.
- **Group by time alone.** Unrelated failures in the same minutes would merge into one incident.
- **Group in Query, where relations live.** Grouping has to happen when an alert opens, in the evaluator's
  transaction, which is Alerting's. The Core interface keeps the modules apart.
- **Acknowledge stops notifications at once.** Useful, but it changes delivery rules that ADR 0009 settled. It needs
  its own decision with escalation.

## Consequences

- Each alert that opens costs one more insert and a lookup of the open incidents of its environment, which are few.
- Incidents and alerts share retention.
- The context read touches traces, logs, and metrics. It stays within the speed budgets of ADR 0015 by reading only
  the window and the affected blocks.
- Runbook links are settings, so Admins change them without a restart.
