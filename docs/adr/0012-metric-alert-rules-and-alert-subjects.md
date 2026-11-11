# 0012. Metric alert rules, alerts on hosts, and alert history

Date: 2026-11-10
Status: Accepted

## Context

Alerting has one built-in rule: the error rate of a service's logs (VEY-17, ADR 0009 for notifications). Metrics now
arrive (ADR 0010), and the home already shows two things from them that people want to be told about: disks that fill
up and slow services. Three facts shape how to add rules for them:

- **Alerts belong to services today.** An alert has a required service name, one open alert per rule and service, and
  the home links it to the service's block. A disk belongs to a host and one of its mount points.
- **The numbers already exist in one place.** The Metrics screens and the home compute a disk's days until full and a
  service's p95 latency in the Query module. Modules may not reference each other (AGENTS.md); a second copy of the
  same computation in Alerting would drift from what the screens show.
- **History is thin.** Resolved alerts are kept 30 days and only "resolved today" is shown. There is no list of past
  alerts.

## Decision

An alert belongs to a **block** (ADR 0007): a service or a host, optionally with a **part** such as a mount point.
Three **built-in metric rules** join the error-rate rule. Rules read their numbers through a **Core interface** the
Query module implements. Resolved alerts are kept **90 days** and listed on an **Alerts screen**.

### Alert subjects

- `alerting.alert` gets `block_kind` (`service` or `host`) and `part`, and `service_name` becomes `block_name`.
  Existing alerts become service alerts. One open alert per rule, environment, block, and part.
- The home links an alert to its block's identity, `service:{environment}/{name}` or `host:{environment}/{name}`.
  Host blocks with alerts appear on the home like service blocks.
- Notifications and the webhook document say what the alert is about in words that fit the rule, for example "Disk
  / on web-01 is full in 9 days". The webhook document gains `block` (`kind`, `name`, `part`); `service` stays for
  service alerts, so existing receivers keep working.

### The rules

| Rule | Measures | Caution | Warning | Clears |
| --- | --- | --- | --- | --- |
| `disk-space` | Share of a disk in use, now | 90 % | 95 % | 5 points below the threshold |
| `disk-filling` | Days until full at the pace of the last day | 14 days | 3 days | more than 21 days, or not filling |
| `latency-p95` | A service's p95 over 5 minutes, at least 20 requests | 1 s | 3 s | below 80 % of the threshold |

- Every number is a setting under `Veyra:Alerting` (`DiskSpace`, `DiskFilling`, `Latency`), and each rule can be
  switched off, like `ErrorRate`.
- Rules run in the evaluation that already runs every 30 seconds. A metric rule that has no data for a block keeps an
  open alert as it is, as the error-rate rule does with no records, and resolves it once data shows it clear.
- Pseudo and read-only file systems never alert; they are already left out of the disk figures.

### Reading metrics

- A new interface in `Veyra.Core`, `IMetricHealth`, returns disk outlooks per host and mount point and p95 latencies
  per service. The Query module implements it with the code that draws the Metrics screens and the home, so an alert
  and the screen it links to always agree. Alerting depends only on the interface.

### History

- Resolved alerts are kept for `Veyra:Alerting:HistoryDays`, 90 by default, instead of 30.
- `GET /api/v1/alerts` lists alerts, newest first, with keyset paging and filters for state, rule, severity,
  environment, block, and time; `GET /api/v1/alerts/summary` counts them per rule and per day for the same period.
  The details endpoint shows each rule's own measure: the error ratio, a disk's use with its projection, or p95
  latency with the slowest routes.
- An alert remembers the worst severity it ever had, so the history can say "was warning" after it eased.

## Alternatives considered

- **A separate host-alert table.** Keeps the service alert untouched, but splits every list, count, and notification
  in two.
- **The service name holding a host name.** No migration, but the home would show hosts as services and the names
  would collide.
- **Computing disk trends and p95 again in Alerting.** No new interface, but two implementations of numbers people
  compare side by side.
- **User-defined rules now.** What people eventually want, but it needs a rule language, an editor, and an ADR of its
  own. Built-in rules with settings cover the three cases the home already raises.
- **Alerting on `disk-filling` alone.** A full disk that stopped growing would never alert.

## Consequences

- One migration in Alerting renames a column and adds two; the open-alert index changes with it.
- The REST answers of alerts and the home name the block instead of only the service; the frontend follows.
- The evaluation now also reads metric figures, so a slow metric query slows it; the figures cover the last five
  minutes and the last day, as on the screens.
- 90 days of resolved alerts is small: a few rows per day even on a busy installation.
